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
        public Categories Category { get; set; }

        [NotMapped]
        public double similarityScore { get; set; }

    }

    public enum Categories
    {
        Science,
        Mathematics,
        Commerce,
        Arts,
        Fantasy,
        Thriller,
        Crime,
        Suspense,
        Fiction,
        Kids,
        Biography,
        Business,
        Health,
        Cooking,
        Horror,
        Romance,
        Social_Science,
        Travel,
        Sports,
        Agriculture,
        Self_Help,
        Unknown
    }
}
