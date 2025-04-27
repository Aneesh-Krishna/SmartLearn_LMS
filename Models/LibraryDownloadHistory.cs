namespace ClassroomAPI.Models
{
    public class LibraryDownloadHistory
    {
        public Guid LibraryDownloadHistoryId { get; set; } = Guid.NewGuid();
        public string DownloaderId { get; set; } = string.Empty;
        public ApplicationUser? DownloaderUser { get; set; } 
        public Guid LibraryMaterialId { get; set; }
        public LibraryMaterialUpload? LibraryMaterial { get; set; }
        public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
    }
}
