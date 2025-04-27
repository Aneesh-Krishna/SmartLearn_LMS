using System.ComponentModel.DataAnnotations.Schema;

namespace ClassroomAPI.Models
{
    public class LibraryMaterialUpload
    {
        public Guid LibraryMaterialUploadId { get; set; }
        public string LibraryMaterialUploadName { get; set; } = string.Empty;
        public string LibraryMaterialUploadUrl { get; set; } = string.Empty;
        public string UploaderId { get; set; } = string.Empty;
        public string AcceptedOrRejected { get; set; } = string.Empty;
        public ApplicationUser? Uploader { get; set; }

        [NotMapped]
        public double similarrityScore { get; set; }
    }
}
