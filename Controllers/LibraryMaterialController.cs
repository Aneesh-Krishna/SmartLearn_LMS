using ClassroomAPI.Data;
using ClassroomAPI.Models;
using ClassroomAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ClassroomAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class LibraryMaterialController : ControllerBase
    {
        private readonly ClassroomDbContext _context;
        private readonly FileService _fileService;

        public LibraryMaterialController(ClassroomDbContext context, FileService fileService)
        {
            _context = context;
            _fileService = fileService;
        }

        //Endpoint to get all library-materials (Only accepted)
        [HttpGet]
        public async Task<IActionResult> GetAllLibraryMaterials()
        {
            var userId = GetCurrentUserID();
            if(userId == null)
            {
                return Unauthorized("User Id not found!");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if(user == null)
            {
                return NotFound("User not found!");
            }

            var libraryMaterials = await _context.LibraryMaterials
                .Where(lm => lm.AcceptedOrRejected == "Accepted")
                .Select(lm => new
                {
                    lm.LibraryMaterialUploadId,
                    lm.LibraryMaterialUploadName,
                    lm.LibraryMaterialUploadUrl,
                    lm.UploaderId,
                    Uploader = lm.Uploader.FullName ?? ""
                })
                .ToListAsync();

            return Ok(libraryMaterials);
        }

        //Get only upload pending library materials
        [HttpGet("getUploadPendingMaterials")]
        public async Task<IActionResult> GetUploadPendingMaterials()
        {
            var userId = GetCurrentUserID();
            if (userId == null)
                return Unauthorized("Please login");

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return NotFound("User not found!");

            if (user.Role != Roles.Admin)
                return Unauthorized("You're not authorized!");

            var uploadPendingMaterials = await _context.LibraryMaterials
                .Where(lm => lm.AcceptedOrRejected == string.Empty)
                .Select(lm => new
                {
                    lm.LibraryMaterialUploadId,
                    lm.LibraryMaterialUploadName,
                    lm.LibraryMaterialUploadUrl,
                    lm.UploaderId,
                    lm.AcceptedOrRejected,
                    uploaderName = lm.Uploader.FullName,
                    uploaderUserName = lm.Uploader.UserName
                })
                .ToListAsync();

            return Ok(uploadPendingMaterials);
        }

        //Endpoint to get a specific library material
        [HttpGet("{libraryMaterialId}/getMaterialById")]
        public async Task<IActionResult> GetMaterial(Guid libraryMaterialId)
        {
            var userId = GetCurrentUserID();
            if (userId == null)
            {
                return Unauthorized("User Id not found!");
            }


            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound("User not found!");
            }

            var libraryMaterial = await _context.LibraryMaterials.FirstOrDefaultAsync(lm => lm.LibraryMaterialUploadId == libraryMaterialId);
            if (libraryMaterial == null)
                return NotFound("Material not found");

            var returnLibraryMaterial = new
            {
                LibraryMaterialUploadId = libraryMaterial.LibraryMaterialUploadId,
                LibraryMaterialUploadName = libraryMaterial.LibraryMaterialUploadName,
                LibraryMaterialUploadUrl = libraryMaterial.LibraryMaterialUploadUrl,
                UploaderId = libraryMaterial.UploaderId,
                Uploader = libraryMaterial.Uploader.FullName ?? ""
            };

            return Ok(returnLibraryMaterial);
        }

        //Endpoint to get library-materials uploaded by a specific user
        [HttpGet("{uploaderId}/getMaterialByUser")]
        public async Task<IActionResult> GetMaterialsByUser(string uploaderId)
        {
            var userId = GetCurrentUserID();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return NotFound("User not found!");
            }

            if(user.Role != Roles.Admin)
            {
                return Unauthorized("You're not authorized!");
            }

            var uploader = await _context.Users.FirstOrDefaultAsync(u => u.Id == uploaderId);
            if (uploader == null)
                return BadRequest("Uploader not found!");

            var libraryMaterials = await _context.LibraryMaterials
                .Where(lm => lm.UploaderId == uploaderId)
                .Select(lm => new
                {
                    lm.LibraryMaterialUploadId,
                    lm.LibraryMaterialUploadName,
                    lm.LibraryMaterialUploadUrl,
                    lm.UploaderId,
                    Uploader = lm.Uploader.FullName ?? ""
                })
                .ToListAsync();

            if (libraryMaterials == null)
                return NoContent();

            return Ok(libraryMaterials);
        }

        //Endpoint for Material Uploading
        [HttpPost("{Category}/upload-library-material")]
        public async Task<IActionResult> UploadLibraryMaterial(IFormFile file, string Category)
        {
            var userId = GetCurrentUserID();

            if (userId == null)
                return Unauthorized("User Id not found!");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found!");

            var fileUrl = await _fileService.UploadFileAsync(file);

            var libraryMaterial = new LibraryMaterialUpload
            {
                LibraryMaterialUploadId = Guid.NewGuid(),
                LibraryMaterialUploadName = file.FileName,
                LibraryMaterialUploadUrl = fileUrl,
                UploaderId = userId,
                AcceptedOrRejected = "",
                Uploader = user,
                Category = Enum.TryParse<Categories>(Category, ignoreCase: true, out var parsedCategory) ? parsedCategory : Categories.Unknown
            };

            _context.LibraryMaterials.Add(libraryMaterial);
            await _context.SaveChangesAsync();

            var returnLibraryMaterial = new
            {
                LibraryMaterialUploadId = libraryMaterial.LibraryMaterialUploadId,
                LibraryMaterialUploadName = libraryMaterial.LibraryMaterialUploadName,
                LibraryMaterialUploadUrl = libraryMaterial.LibraryMaterialUploadUrl,
                UploaderId = libraryMaterial.UploaderId,
                Uploader = libraryMaterial.Uploader.FullName ?? "",
                Category
            };

            return Ok(returnLibraryMaterial);
        }

        //Endpoint for accepting the material(Only for application's admin)
        [HttpPut("{libraryMaterialId}/Accept")]
        public async Task<IActionResult> AcceptLibraryMaterial(Guid libraryMaterialId, [FromBody] string? Category)
        {
            var userId = GetCurrentUserID();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found!");

            if (user.Role != Roles.Admin)
                return Unauthorized("You're not authorized!");

            var libraryMaterial = await _context.LibraryMaterials.FirstOrDefaultAsync(lm => lm.LibraryMaterialUploadId == libraryMaterialId);
            if (libraryMaterial == null)
                return NotFound("Material not found!");

            libraryMaterial.AcceptedOrRejected = "Accepted";
            if (Category != "")
                libraryMaterial.Category = Enum.TryParse<Categories>(Category, ignoreCase: true, out var parsedCategory) ? parsedCategory : libraryMaterial.Category;

            await _context.SaveChangesAsync();

            var returnLibraryMaterial = new
            {
                LibraryMaterialUploadId = libraryMaterial.LibraryMaterialUploadId,
                LibraryMaterialUploadName = libraryMaterial.LibraryMaterialUploadName,
                LibraryMaterialUploadUrl = libraryMaterial.LibraryMaterialUploadUrl,
                UploaderId = libraryMaterial.UploaderId,
                Uploader = libraryMaterial.Uploader?.FullName ?? "",
                Category
            };

            return Ok(returnLibraryMaterial);
        }

        //Endpoint for rejecting the material(Only for application's admin)
        [HttpDelete("{libraryMaterialId}/Reject")]
        public async Task<IActionResult> RejectLibraryMaterial(Guid libraryMaterialId)
        {
            var userId = GetCurrentUserID();
            if (userId == null)
                return Unauthorized("User Id not found!");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound("User not found!");

            if (user.Role != Roles.Admin)
                return Unauthorized("You're not authorized!");

            var libraryMaterial = await _context.LibraryMaterials.FirstOrDefaultAsync(lm => lm.LibraryMaterialUploadId == libraryMaterialId);
            if (libraryMaterial == null)
                return NotFound("Material not found!");

            //libraryMaterial.AcceptedOrRejected = "Rejected";

            _context.LibraryMaterials.Remove(libraryMaterial);
            await _context.SaveChangesAsync();

            return Ok("Material has been rejected!");
        }

        //Endpoint for updating the downloads history
        [HttpPost("{libraryMaterialId}/downloadLibraryMaterialId")]
        public async Task<IActionResult> DownloadLibraryMaterial(Guid libraryMaterialId)
        {
            var userId = GetCurrentUserID();
            if (userId == null) return Unauthorized("Please login");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound("User not found!");

            var libraryMaterial = await _context.LibraryMaterials.FirstOrDefaultAsync(lm => lm.LibraryMaterialUploadId == libraryMaterialId);
            if (libraryMaterial == null)
                return NotFound("No such library-material found!");

            var libraryDownloadHistory = new LibraryDownloadHistory
            {
                LibraryDownloadHistoryId = Guid.NewGuid(),
                LibraryMaterialId = libraryMaterialId,
                LibraryMaterial = libraryMaterial,
                DownloaderId = userId,
                DownloaderUser = user,
                DownloadedAt = DateTime.UtcNow
            };

            _context.LibraryDownloadHistory.Add(libraryDownloadHistory);
            await _context.SaveChangesAsync();

            return Ok("Download history saved!");
        }

        [HttpGet("enhanced-recommendations")]
        public async Task<IActionResult> GetEnhancedRecommendations()
        {
            try
            {
                // Get current user ID from token
                var currentUserId = GetCurrentUserID();
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return Unauthorized("User not found");
                }

                // Get the user
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
                if (currentUser == null)
                {
                    return NotFound("User not found");
                }

                // Get user's download history with material details
                var userDownloadHistory = await _context.LibraryDownloadHistory
                    .Where(dh => dh.DownloaderId == currentUserId)
                    .Include(dh => dh.LibraryMaterial)
                    .OrderByDescending(dh => dh.DownloadedAt)
                    .ToListAsync();

                // If user has no downloads, return materials based on popularity and recency
                if (!userDownloadHistory.Any())
                {
                    var popularMaterials = await GetPopularAndRecentMaterials();
                    return Ok(new
                    {
                        recommendationType = "popular",
                        message = "Recommendations based on popular materials",
                        recommendations = popularMaterials
                    });
                }

                // Content-based filtering: Get materials with similar categories to what user has downloaded
                var contentBasedRecommendations = await GetContentBasedRecommendations(userDownloadHistory, currentUserId);

                // Collaborative filtering: Get recommendations based on similar users
                //var collaborativeRecommendations = await GetCollaborativeRecommendations(userDownloadHistory, currentUserId);

                // Combine both recommendation types
                //var combinedRecommendations = MergeAndRankRecommendations(contentBasedRecommendations, collaborativeRecommendations);
                var combinedRecommendations = MergeAndRankRecommendations(contentBasedRecommendations, contentBasedRecommendations);

                return Ok(new
                {
                    recommendationType = "personalized",
                    message = "Personalized recommendations based on your interests",
                    recommendations = combinedRecommendations.Take(10).ToList()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // Get popular materials for new users
        private async Task<List<object>> GetPopularAndRecentMaterials()
        {
            // Get download counts for all materials
            var materialDownloadCounts = await _context.LibraryDownloadHistory
                .GroupBy(dh => dh.LibraryMaterialId)
                .Select(g => new { MaterialId = g.Key, DownloadCount = g.Count() })
                .ToListAsync();

            // Get accepted materials
            var acceptedMaterials = await _context.LibraryMaterials
                .Where(m => m.AcceptedOrRejected == "Accepted")
                .Include(m => m.Uploader)
                .ToListAsync();

            // Calculate popularity score (combination of recency and download count)
            var currentDate = DateTime.UtcNow;
            var recommendationsWithScores = acceptedMaterials.Select(material =>
            {
                // Find download count for this material
                var downloadInfo = materialDownloadCounts.FirstOrDefault(m => m.MaterialId == material.LibraryMaterialUploadId);
                var downloadCount = downloadInfo?.DownloadCount ?? 0;

                // Calculate recency factor (higher for newer materials)
                // This assumes LibraryMaterial has a CreatedAt property - you might need to adapt this
                var daysSinceCreation = 30; // Default value if no creation date

                // Calculate popularity score
                double popularityScore = (downloadCount * 0.7) + ((30 - daysSinceCreation) * 0.3);

                return new
                {
                    material.LibraryMaterialUploadId,
                    material.LibraryMaterialUploadName,
                    material.LibraryMaterialUploadUrl,
                    UploaderId = material.UploaderId,
                    Uploader = material.Uploader?.FullName ?? "",
                    Category = material.Category.ToString(),
                    DownloadCount = downloadCount,
                    RecommendationScore = popularityScore,
                    RecommendationType = "popular"
                };
            })
            .OrderByDescending(m => m.RecommendationScore)
            .Take(10)
            .ToList<object>();

            return recommendationsWithScores;
        }

        // Get content-based recommendations (based on categories user has shown interest in)
        private async Task<List<LibraryMaterialUpload>> GetContentBasedRecommendations(
            List<LibraryDownloadHistory> userDownloadHistory,
            string currentUserId)
        {
            // Extract categories from user's downloaded materials
            var userPreferredCategories = userDownloadHistory
                .Where(dh => dh.LibraryMaterial != null)
                .GroupBy(dh => dh.LibraryMaterial.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .Take(3)  // Top 3 categories
                .ToList();

            // Get materials in user's preferred categories that they haven't downloaded yet
            var downloadedMaterialIds = userDownloadHistory.Select(dh => dh.LibraryMaterialId).ToList();

            var contentBasedRecommendations = new List<LibraryMaterialUpload>();

            foreach (var categoryPreference in userPreferredCategories)
            {
                var materialsInCategory = await _context.LibraryMaterials
                    .Where(m => m.AcceptedOrRejected == "Accepted"
                           && m.Category == categoryPreference.Category
                           && !downloadedMaterialIds.Contains(m.LibraryMaterialUploadId))
                    .Include(m => m.Uploader)
                    .ToListAsync();

                // Add similarity score based on category preference strength
                foreach (var material in materialsInCategory)
                {
                    material.similarityScore = (double)categoryPreference.Count / userDownloadHistory.Count;
                    contentBasedRecommendations.Add(material);
                }
            }

            return contentBasedRecommendations;
        }

        // Get collaborative filtering recommendations (based on similar users)
        private async Task<List<LibraryMaterialUpload>> GetCollaborativeRecommendations(
            List<LibraryDownloadHistory> userDownloadHistory,
            string currentUserId)
        {
            // Get materials downloaded by the current user
            var userMaterialIds = userDownloadHistory.Select(dh => dh.LibraryMaterialId).ToList();

            // Find similar users (users who downloaded at least one material that the current user downloaded)
            var similarUserIds = await _context.LibraryDownloadHistory
                .Where(dh => userMaterialIds.Contains(dh.LibraryMaterialId) && dh.DownloaderId != currentUserId)
                .Select(dh => dh.DownloaderId)
                .Distinct()
                .ToListAsync();

            if (!similarUserIds.Any())
            {
                return new List<LibraryMaterialUpload>();
            }

            // Calculate user similarity scores
            var userSimilarityScores = new Dictionary<string, double>();

            foreach (var similarUserId in similarUserIds)
            {
                // Get materials downloaded by similar user
                var similarUserMaterials = await _context.LibraryDownloadHistory
                    .Where(dh => dh.DownloaderId == similarUserId)
                    .Select(dh => dh.LibraryMaterialId)
                    .ToListAsync();

                // Calculate Jaccard similarity (intersection over union)
                var commonMaterials = similarUserMaterials.Intersect(userMaterialIds).Count();
                var unionMaterials = similarUserMaterials.Union(userMaterialIds).Count();

                var similarityScore = (double)commonMaterials / unionMaterials;
                userSimilarityScores[similarUserId] = similarityScore;
            }

            // Get materials downloaded by similar users but not by current user
            var recommendedMaterialIds = await _context.LibraryDownloadHistory
                .Where(dh => similarUserIds.Contains(dh.DownloaderId) && !userMaterialIds.Contains(dh.LibraryMaterialId))
                .Select(dh => new { dh.LibraryMaterialId, dh.DownloaderId })
                .ToListAsync();

            // Calculate weighted recommendation scores
            var recommendationScores = recommendedMaterialIds
                .GroupBy(r => r.LibraryMaterialId)
                .Select(g => new {
                    MaterialId = g.Key,
                    Score = g.Sum(r => userSimilarityScores.ContainsKey(r.DownloaderId) ? userSimilarityScores[r.DownloaderId] : 0)
                })
                .OrderByDescending(r => r.Score)
                .ToList();

            // Get material details
            var collaborativeRecommendations = new List<LibraryMaterialUpload>();

            foreach (var recommendation in recommendationScores)
            {
                var material = await _context.LibraryMaterials
                    .Include(m => m.Uploader)
                    .FirstOrDefaultAsync(m => m.LibraryMaterialUploadId == recommendation.MaterialId
                                           && m.AcceptedOrRejected == "Accepted");

                if (material != null)
                {
                    material.similarityScore = recommendation.Score;
                    collaborativeRecommendations.Add(material);
                }
            }

            return collaborativeRecommendations;
        }

        // Merge and rank recommendations from different methods
        private List<object> MergeAndRankRecommendations(
            List<LibraryMaterialUpload> contentBasedRecommendations,
            List<LibraryMaterialUpload> collaborativeRecommendations)
        {
            // Combine all recommendations
            var allRecommendations = new Dictionary<Guid, LibraryMaterialUpload>();
            var allScores = new Dictionary<Guid, Dictionary<string, double>>();

            // Process content-based recommendations
            foreach (var material in contentBasedRecommendations)
            {
                allRecommendations[material.LibraryMaterialUploadId] = material;

                if (!allScores.ContainsKey(material.LibraryMaterialUploadId))
                {
                    allScores[material.LibraryMaterialUploadId] = new Dictionary<string, double>();
                }

                allScores[material.LibraryMaterialUploadId]["contentBased"] = material.similarityScore;
            }

            // Process collaborative recommendations
            foreach (var material in collaborativeRecommendations)
            {
                allRecommendations[material.LibraryMaterialUploadId] = material;

                if (!allScores.ContainsKey(material.LibraryMaterialUploadId))
                {
                    allScores[material.LibraryMaterialUploadId] = new Dictionary<string, double>();
                }

                allScores[material.LibraryMaterialUploadId]["collaborative"] = material.similarityScore;
            }

            // Calculate final scores and create result objects
            var result = allRecommendations.Select(pair =>
            {
                var material = pair.Value;
                var scores = allScores[pair.Key];

                // Calculate hybrid score with weights
                double contentBasedScore = scores.ContainsKey("contentBased") ? scores["contentBased"] : 0;
                double collaborativeScore = scores.ContainsKey("collaborative") ? scores["collaborative"] : 0;

                // Weighted average - can adjust weights based on performance
                double finalScore = (contentBasedScore * 0.4) + (collaborativeScore * 0.6);

                // Determine primary recommendation type
                string recommendationType = contentBasedScore > collaborativeScore ? "content" : "collaborative";

                return new
                {
                    material.LibraryMaterialUploadId,
                    material.LibraryMaterialUploadName,
                    material.LibraryMaterialUploadUrl,
                    material.UploaderId,
                    Uploader = material.Uploader?.FullName ?? "",
                    Category = material.Category.ToString(),
                    RecommendationScore = finalScore,
                    RecommendationType = recommendationType
                };
            })
            .OrderByDescending(r => r.RecommendationScore)
            .ToList<object>();

            return result;
        }

        //Method for uploading the material
        public async Task<IActionResult> UploadMaterial(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is empty");
            }

            var fileUrl = await _fileService.UploadFileAsync(file);
            return Ok(new { Url = fileUrl });
        }

        private string GetCurrentUserID()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
