using ClassroomAPI.Data;
using ClassroomAPI.Hubs;
using ClassroomAPI.Models;
using Hangfire;
using iTextSharp.text.pdf;
using iTextSharp.text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Azure.Storage.Blobs;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClassroomAPI.Services
{
    public class ReportService
    {
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<ChatHub> _chatHubContext;
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly IHubContext<ChatHub> _hubContext;
        public ReportService(IBackgroundJobClient backgroundJobClient, IServiceProvider serviceProvider, IHubContext<ChatHub> chatHubContext, IConfiguration configuration, IHubContext<ChatHub> hubContext)
        {
            _backgroundJobClient = backgroundJobClient;
            _serviceProvider = serviceProvider;
            _chatHubContext = chatHubContext;
            _bucketName = configuration["AWS:S3BucketName"];
            _s3Client = new AmazonS3Client(configuration["AWS:AccessKey"], configuration["AWS:SecretKey"], Amazon.RegionEndpoint.GetBySystemName(configuration["AWS:Region"]));
            _hubContext = hubContext;
        }

        public async Task GenerateReport(Guid quizId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<ClassroomDbContext>();

                var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.QuizId == quizId);
                if (quiz == null)
                {
                    Console.WriteLine("Quiz not found!");
                    return;
                }
                var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == quiz.CourseId);
                if (course == null)
                {
                    Console.WriteLine("Course not found!");
                    return;
                }

                var adminName = await _context.Users
                    .Where(u => u.Id == course.AdminId)
                    .Select(u => u.FullName)
                    .SingleOrDefaultAsync();
                if (adminName == null)
                    adminName = "Instructor";

                using (var memoryStream = new MemoryStream())
                {
                    var document = new Document();
                    PdfWriter.GetInstance(document, memoryStream).CloseStream = false;
                    document.Open();
                    string heading = "Quiz Report of: " + quiz.Title;
                    document.Add(new Paragraph(heading));

                    var members = await _context.CourseMembers
                        .Where(cm => cm.CourseId == quiz.CourseId)
                        .ToListAsync();
                    if (members == null)
                    {
                        Console.WriteLine("No members in the course!");
                        return;
                    }
                    foreach (var member in members)
                    {
                        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == member.UserId);
                        if (user == null)
                            continue;

                        var response = await _context.QuizResponses
                            .FirstOrDefaultAsync(qr => qr.QuizId == quiz.QuizId && qr.UserId == user.Id);

                        document.Add(new Paragraph($"User: {user.FullName}, Score: {response?.Score ?? 0}, Status: {(response == null ? "Absent" : "Present")}"));

                    }

                    document.Close();
                    memoryStream.Position = 0;

                    var fileName = $"{quiz.Title}_Report.pdf";
                    var reportUrl = await UploadReportToS3Storage(memoryStream, fileName);

                    if (!string.IsNullOrEmpty(reportUrl))
                    {
                        var reportMessage = new Chat
                        {
                            ChatId = Guid.NewGuid(),
                            Message = "Quiz-Report",
                            FileUrl = reportUrl,
                            FileName = fileName,
                            CourseId = course.CourseId,
                            Course = course,
                            UserId = course.AdminId,
                            User = course.GroupAdmin,
                            SenderName = adminName,
                            SentAt = DateTime.Now
                        };

                        _context.Chats.Add(reportMessage);

                        var realTimeChat = new
                        {
                            ChatId = reportMessage.ChatId,
                            CourseId = reportMessage.CourseId,
                            CourseName = course.CourseName,
                            UserId = reportMessage.UserId,
                            SenderName = reportMessage.SenderName,
                            SentAt = reportMessage.SentAt,
                            Message = reportMessage.Message,
                            FileName = reportMessage.FileName,
                            FileUrl = reportMessage.FileUrl
                        };

                        await _hubContext.Clients.Group(course.CourseId.ToString()).SendAsync("ReceiveMessage", realTimeChat);

                        var newReport = new Report
                        {
                            ReportId = Guid.NewGuid(),
                            reportUrl = reportUrl,
                            QuizId = quiz.QuizId,
                            Quiz = quiz
                        };
                        _context.Reports.Add(newReport);

                        quiz.isReportGenerated = true;

                        await _context.SaveChangesAsync();

                        // Notify the group with the report URL
                        await _chatHubContext.Clients.Group(course.CourseId.ToString()).SendAsync("ReceiveReport", reportUrl);
                    }
                    else
                    {
                        Console.WriteLine("Failed to upload report to storage.");
                    }
                }
            }
        }

        public async Task GenerateAnalysisReport(Guid quizId)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<ClassroomDbContext>();

                var quiz = await _context.Quizzes
                    .Include(q => q.Questions)
                    .FirstOrDefaultAsync(q => q.QuizId == quizId);

                if (quiz == null)
                {
                    Console.WriteLine("Quiz not found!");
                    return;
                }

                var course = await _context.Courses.FirstOrDefaultAsync(c => c.CourseId == quiz.CourseId);
                if (course == null)
                {
                    Console.WriteLine("Course not found!");
                    return;
                }

                var adminName = await _context.Users
                    .Where(u => u.Id == course.AdminId)
                    .Select(u => u.FullName)
                    .SingleOrDefaultAsync();
                if (adminName == null)
                    adminName = "Instructor";

                // Get all questions for this quiz
                var questions = await _context.Questions
                    .Where(q => q.QuizId == quizId)
                    .ToListAsync();

                // Calculate total possible points
                int totalPossiblePoints = questions.Sum(q => q.Points);

                // Get all responses for this quiz  
                var responses = await _context.QuizResponses
                    .Where(r => r.QuizId == quizId)
                    .ToListAsync();

                // Get all answers for these responses
                var allAnswers = await _context.Answers
                    .Where(a => responses.Select(r => r.QuizResponseId).Contains(a.QuizResponseId))
                    .ToListAsync();

                // Get all options
                var allOptions = await _context.Options
                    .Where(o => questions.Select(q => q.QuestionId).Contains(o.QuestionId))
                    .ToListAsync();

                using (var memoryStream = new MemoryStream())
                {
                    var document = new Document();
                    PdfWriter.GetInstance(document, memoryStream).CloseStream = false;
                    document.Open();

                    // Create font styles
                    var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                    var headingFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14);
                    var subheadingFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                    var normalFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                    // Add Quiz Information
                    document.Add(new Paragraph($"Quiz Analysis Report: {quiz.Title}", titleFont));
                    document.Add(new Paragraph($"Course: {course.CourseName}", normalFont));
                    document.Add(new Paragraph($"Generated on: {DateTime.Now.ToString("yyyy-MM-dd HH:mm")}\n\n", normalFont));

                    // Add Overall Statistics
                    document.Add(new Paragraph("OVERALL STATISTICS", headingFont));
                    int totalStudents = await _context.CourseMembers.CountAsync(cm => cm.CourseId == quiz.CourseId);
                    int studentsAttempted = responses.Select(r => r.UserId).Distinct().Count();
                    int studentsNotAttempted = totalStudents - studentsAttempted;

                    document.Add(new Paragraph($"Total students in course: {totalStudents}", normalFont));
                    document.Add(new Paragraph($"Students who attempted the quiz: {studentsAttempted} ({(totalStudents > 0 ? (studentsAttempted * 100.0 / totalStudents).ToString("0.0") : "0")}%)", normalFont));
                    document.Add(new Paragraph($"Students who did not attempt: {studentsNotAttempted} ({(totalStudents > 0 ? (studentsNotAttempted * 100.0 / totalStudents).ToString("0.0") : "0")}%)\n\n", normalFont));

                    // Calculate average score - CORRECTED CALCULATION
                    double averageScore = responses.Count > 0 ? responses.Average(r => r.Score) : 0;

                    // Display average score per student as percentage of the total possible points
                    document.Add(new Paragraph($"Average Score: {averageScore.ToString("0.00")} out of {totalPossiblePoints} ({(totalPossiblePoints > 0 ? (averageScore * 100.0 / totalPossiblePoints).ToString("0.0") : "0")}%)", normalFont));

                    // Score Distribution - CORRECTED CALCULATION
                    // Now calculated as percentage of total points per question, not factoring in students
                    document.Add(new Paragraph("Score Distribution:", normalFont));

                    var scoreRanges = new[] {
                new { Range = "0-20%", Count = 0 },
                new { Range = "21-40%", Count = 0 },
                new { Range = "41-60%", Count = 0 },
                new { Range = "61-80%", Count = 0 },
                new { Range = "81-100%", Count = 0 }
            }.ToList();

                    foreach (var response in responses)
                    {
                        // Calculate each student's score as percentage of total possible points
                        double percentage = totalPossiblePoints > 0 ? (response.Score * 100.0 / totalPossiblePoints) : 0;

                        if (percentage <= 20) scoreRanges[0] = new { Range = scoreRanges[0].Range, Count = scoreRanges[0].Count + 1 };
                        else if (percentage <= 40) scoreRanges[1] = new { Range = scoreRanges[1].Range, Count = scoreRanges[1].Count + 1 };
                        else if (percentage <= 60) scoreRanges[2] = new { Range = scoreRanges[2].Range, Count = scoreRanges[2].Count + 1 };
                        else if (percentage <= 80) scoreRanges[3] = new { Range = scoreRanges[3].Range, Count = scoreRanges[3].Count + 1 };
                        else scoreRanges[4] = new { Range = scoreRanges[4].Range, Count = scoreRanges[4].Count + 1 };
                    }

                    // Display distribution of students across score ranges
                    foreach (var range in scoreRanges)
                    {
                        // This shows the percentage of students within each score range
                        document.Add(new Paragraph($"  • {range.Range}: {range.Count} students ({(responses.Count > 0 ? (range.Count * 100.0 / responses.Count).ToString("0.0") : "0")}%)", normalFont));
                    }

                    document.Add(new Paragraph("\n"));

                    // Question-by-Question Analysis
                    document.Add(new Paragraph("QUESTION-BY-QUESTION ANALYSIS", headingFont));

                    foreach (var question in questions)
                    {
                        document.Add(new Paragraph($"Question: {question.Text}", subheadingFont));
                        document.Add(new Paragraph($"Difficulty Level: {question.Difficulty} - Points: {question.Points}", normalFont));

                        // Get options for this question
                        var options = allOptions.Where(o => o.QuestionId == question.QuestionId).ToList();

                        // Get all answers for this question
                        var answersForQuestion = allAnswers
                            .Where(a => options.Select(o => o.OptionId).Contains(a.OptionId))
                            .ToList();

                        // Calculate how many students answered this question
                        int studentsAnswered = answersForQuestion.Select(a => a.QuizResponseId).Distinct().Count();

                        document.Add(new Paragraph($"Students who answered: {studentsAnswered} ({(studentsAttempted > 0 ? (studentsAnswered * 100.0 / studentsAttempted).ToString("0.0") : "0")}%)", normalFont));

                        // Calculate correct answers percentage
                        var correctOptions = options.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList();
                        var correctAnswers = answersForQuestion.Count(a => correctOptions.Contains(a.OptionId));

                        document.Add(new Paragraph($"Correct answers: {correctAnswers} ({(studentsAnswered > 0 ? (correctAnswers * 100.0 / studentsAnswered).ToString("0.0") : "0")}%)", normalFont));

                        // Option breakdown
                        document.Add(new Paragraph("Option breakdown:", normalFont));
                        foreach (var option in options)
                        {
                            int optionCount = answersForQuestion.Count(a => a.OptionId == option.OptionId);
                            document.Add(new Paragraph($"  • {option.Text}: {optionCount} selections ({(studentsAnswered > 0 ? (optionCount * 100.0 / studentsAnswered).ToString("0.0") : "0")}%) {(option.IsCorrect ? "[CORRECT]" : "")}", normalFont));
                        }

                        document.Add(new Paragraph("\n"));
                    }

                    // Student Performance Section
                    document.Add(new Paragraph("INDIVIDUAL STUDENT PERFORMANCE", headingFont));

                    var members = await _context.CourseMembers
                        .Where(cm => cm.CourseId == quiz.CourseId)
                        .ToListAsync();

                    PdfPTable table = new PdfPTable(4);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 3f, 1f, 1f, 2f });

                    // Add table headers
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);

                    // Add table headers with styling
                    PdfPCell nameHeaderCell = new PdfPCell(new Phrase("Student Name", headerFont));
                    nameHeaderCell.BackgroundColor = BaseColor.LightGray;
                    table.AddCell(nameHeaderCell);

                    PdfPCell scoreHeaderCell = new PdfPCell(new Phrase("Score", headerFont));
                    scoreHeaderCell.BackgroundColor = BaseColor.LightGray;
                    table.AddCell(scoreHeaderCell);

                    PdfPCell percentHeaderCell = new PdfPCell(new Phrase("Percentage", headerFont));
                    percentHeaderCell.BackgroundColor = BaseColor.LightGray;
                    table.AddCell(percentHeaderCell);

                    PdfPCell timeHeaderCell = new PdfPCell(new Phrase("Submission Time", headerFont));
                    timeHeaderCell.BackgroundColor = BaseColor.LightGray;
                    table.AddCell(timeHeaderCell);

                    // Add student data
                    foreach (var member in members)
                    {
                        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == member.UserId);
                        if (user == null) continue;

                        var response = responses.FirstOrDefault(r => r.UserId == user.Id);

                        table.AddCell(new PdfPCell(new Phrase(user.FullName ?? "Unknown", normalFont)));

                        if (response != null)
                        {
                            table.AddCell(new PdfPCell(new Phrase(response.Score.ToString(), normalFont)));

                            // Calculate percentage based on total possible points from questions
                            string percentageStr = totalPossiblePoints > 0
                                ? (response.Score * 100.0 / totalPossiblePoints).ToString("0.0")
                                : "0";

                            table.AddCell(new PdfPCell(new Phrase($"{percentageStr}%", normalFont)));
                            table.AddCell(new PdfPCell(new Phrase(response.SubmittedAt.ToString("yyyy-MM-dd HH:mm"), normalFont)));
                        }
                        else
                        {
                            var notAttemptedFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10, BaseColor.Gray);
                            table.AddCell(new PdfPCell(new Phrase("Not Attempted", notAttemptedFont)));
                            table.AddCell(new PdfPCell(new Phrase("0%", notAttemptedFont)));
                            table.AddCell(new PdfPCell(new Phrase("N/A", notAttemptedFont)));
                        }
                    }

                    document.Add(table);

                    // Recommendations section
                    document.Add(new Paragraph("\nRECOMMENDATIONS", headingFont));

                    // Find hardest questions (lowest correct percentage)
                    var questionPerformance = questions.Select(q => {
                        var qOptions = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
                        var qAnswers = allAnswers.Where(a => qOptions.Select(o => o.OptionId).Contains(a.OptionId)).ToList();
                        var correctOpts = qOptions.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList();
                        var correctCount = qAnswers.Count(a => correctOpts.Contains(a.OptionId));
                        var totalCount = qAnswers.Select(a => a.QuizResponseId).Distinct().Count();
                        return new
                        {
                            Question = q,
                            CorrectPercentage = totalCount > 0 ? (correctCount * 100.0 / totalCount) : 0
                        };
                    }).OrderBy(q => q.CorrectPercentage).ToList();

                    if (questionPerformance.Any())
                    {
                        var hardestQuestions = questionPerformance.Take(3).ToList();
                        document.Add(new Paragraph("Topics that need additional attention:", normalFont));

                        foreach (var item in hardestQuestions)
                        {
                            if (item.CorrectPercentage < 70)
                            {
                                document.Add(new Paragraph($"  • \"{item.Question.Text}\" - Only {item.CorrectPercentage.ToString("0.0")}% of students answered correctly", normalFont));
                            }
                        }
                    }

                    document.Close();
                    memoryStream.Position = 0;

                    var fileName = $"{quiz.Title}_AnalysisReport.pdf";
                    var reportUrl = await UploadReportToS3Storage(memoryStream, fileName);

                    if (!string.IsNullOrEmpty(reportUrl))
                    {
                        var reportMessage = new Chat
                        {
                            ChatId = Guid.NewGuid(),
                            Message = "Quiz Analysis Report",
                            FileUrl = reportUrl,
                            FileName = fileName,
                            CourseId = course.CourseId,
                            Course = course,
                            UserId = course.AdminId,
                            User = course.GroupAdmin,
                            SenderName = adminName,
                            SentAt = DateTime.Now
                        };

                        _context.Chats.Add(reportMessage);

                        var realTimeChat = new
                        {
                            ChatId = reportMessage.ChatId,
                            CourseId = reportMessage.CourseId,
                            CourseName = course.CourseName,
                            UserId = reportMessage.UserId,
                            SenderName = reportMessage.SenderName,
                            SentAt = reportMessage.SentAt,
                            Message = reportMessage.Message,
                            FileName = reportMessage.FileName,
                            FileUrl = reportMessage.FileUrl
                        };

                        await _hubContext.Clients.Group(course.CourseId.ToString()).SendAsync("ReceiveMessage", realTimeChat);

                        var newReport = new Report
                        {
                            ReportId = Guid.NewGuid(),
                            reportUrl = reportUrl,
                            QuizId = quiz.QuizId,
                            Quiz = quiz
                        };
                        _context.Reports.Add(newReport);

                        await _context.SaveChangesAsync();

                        // Notify the group with the report URL
                        await _chatHubContext.Clients.Group(course.CourseId.ToString()).SendAsync("ReceiveReport", reportUrl);
                    }
                    else
                    {
                        Console.WriteLine("Failed to upload report to storage.");
                    }
                }
            }
        }

        private async Task<string> UploadReportToS3Storage(Stream reportStream, string fileName)
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                InputStream = reportStream,
                ContentType = "application/pdf",
            };

            await _s3Client.PutObjectAsync(putRequest);
            return $"https://{_bucketName}.s3.amazonaws.com/{fileName}";
        }
    }
}
