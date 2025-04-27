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
        [HttpPost("upload-library-material")]
        public async Task<IActionResult> UploadLibraryMaterial(IFormFile file)
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
                Uploader = user
            };

            _context.LibraryMaterials.Add(libraryMaterial);
            await _context.SaveChangesAsync();

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

        //Endpoint for accepting the material(Only for application's admin)
        [HttpPut("{libraryMaterialId}/Accept")]
        public async Task<IActionResult> AcceptLibraryMaterial(Guid libraryMaterialId)
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

            await _context.SaveChangesAsync();

            var returnLibraryMaterial = new
            {
                LibraryMaterialUploadId = libraryMaterial.LibraryMaterialUploadId,
                LibraryMaterialUploadName = libraryMaterial.LibraryMaterialUploadName,
                LibraryMaterialUploadUrl = libraryMaterial.LibraryMaterialUploadUrl,
                UploaderId = libraryMaterial.UploaderId,
                Uploader = libraryMaterial.Uploader?.FullName ?? ""
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

        [HttpGet("recommendations")]
        public async Task<ActionResult<List<LibraryMaterialUpload>>> GetRecommendations()
        {
            try
            {
                // Get current user ID from token
                var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    return Unauthorized();
                }

                // Step 1: Get user's download history
                var userDownloads = await _context.LibraryDownloadHistory
                    .Where(dh => dh.DownloaderId == currentUserId)
                    .ToListAsync();

                if (!userDownloads.Any())
                {
                    // If user has no downloads, return most popular materials
                    var popularMaterials = await _context.LibraryMaterials
                        .Where(m => m.AcceptedOrRejected == "Accepted")
                        .OrderByDescending(m => _context.LibraryDownloadHistory.Count(dh => dh.LibraryMaterialId == m.LibraryMaterialUploadId))
                        .Take(5)
                        .ToListAsync();

                    return Ok(new { values = popularMaterials });
                }

                // Step 2: Get the IDs of materials downloaded by user
                var downloadedMaterialIds = userDownloads.Select(d => d.LibraryMaterialId).ToList();

                // Step 3: Find other users who downloaded the same materials
                var similarUserIds = await _context.LibraryDownloadHistory
                    .Where(dh => downloadedMaterialIds.Contains(dh.LibraryMaterialId) && dh.DownloaderId != currentUserId)
                    .Select(dh => dh.DownloaderId)
                    .Distinct()
                    .ToListAsync();

                // Step 4: Find materials downloaded by similar users but not by current user
                var recommendationIds = await _context.LibraryDownloadHistory
                    .Where(dh => similarUserIds.Contains(dh.DownloaderId) && !downloadedMaterialIds.Contains(dh.LibraryMaterialId))
                    .GroupBy(dh => dh.LibraryMaterialId)
                    .Select(g => new { MaterialId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .Select(x => x.MaterialId)
                    .ToListAsync();

                // Step 5: Get actual material details
                var recommendations = await _context.LibraryMaterials
                    .Where(m => recommendationIds.Contains(m.LibraryMaterialUploadId) && m.AcceptedOrRejected == "Accepted")
                    .ToListAsync();

                // Calculate similarity scores (simple version - based on download count)
                var totalSimilarUserDownloads = await _context.LibraryDownloadHistory
                    .Where(dh => similarUserIds.Contains(dh.DownloaderId))
                    .CountAsync();

                var recommendationsWithScores = recommendations.Select(r => {
                    var downloadsCount = _context.LibraryDownloadHistory
                        .Count(dh => dh.LibraryMaterialId == r.LibraryMaterialUploadId && similarUserIds.Contains(dh.DownloaderId));

                    // Normalize to 0-1 scale
                    var similarityScore = totalSimilarUserDownloads > 0 ? (double)downloadsCount / totalSimilarUserDownloads : 0;

                    // Add score property
                    //r.SimilarityScore = similarityScore;

                    return r;
                }).ToList();

                return Ok(new { values = recommendationsWithScores });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
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
