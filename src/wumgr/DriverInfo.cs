using System;
using System.Collections.Generic;
using System.Linq;

namespace wumgr
{
    /// <summary>Windows Update Agent 驱动更新的可导出描述。</summary>
    public class DriverInfo
    {
        public string Id { get; set; } = string.Empty;
        public int RevisionNumber { get; set; } = -1;
        public string Title { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = "未提供";
        public string Version { get; set; } = "未提供";
        public string HardwareId { get; set; } = "未提供";
        public string Manufacturer { get; set; } = "未提供";
        public string Provider { get; set; } = "未提供";
        public string DriverClass { get; set; } = "未提供";
        public DateTime DriverDate { get; set; } = DateTime.MinValue;
        /// <summary>WUA 是否将此更新标记为已被后续更新取代；仅表示 WUA 返回的事实。</summary>
        public bool Superseded { get; set; }
        public decimal Size { get; set; } = 0m;
        public List<string> DownloadUrls { get; set; } = new List<string>();
        public string Source { get; set; } = "Windows Update Agent";
        public string DownloadUrlSource { get; set; } = string.Empty;
        public string CatalogUrl { get; set; } = string.Empty;
        public string Products { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public bool CatalogHardwareVerified { get; set; }
        /// <summary>Can start a download, including lazy resolution of a verified Catalog item.</summary>
        public bool CanDownload
        {
            get
            {
                Guid id;
                bool validId = Guid.TryParse(Id, out id);
                if (Source == "Microsoft Update Catalog")
                    return CatalogHardwareVerified && validId && (DownloadUrls == null || DownloadUrls.Count == 0 || DownloadUrls.All(MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl));
                return (DownloadUrls != null && DownloadUrls.Count > 0) || (Source == "Windows Update Agent" && validId);
            }
        }
        public string LocalPath { get; set; } = string.Empty;
        public string Status { get; set; } = "未提供";
    }

    /// <summary>单个驱动导出进度。</summary>
    public class DriverDownloadProgress
    {
        public int Percent { get; set; } = 0;
        public string Message { get; set; } = string.Empty;
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public string ChunkId { get; set; } = string.Empty;
        public double ChunkPercent { get; set; }
        public bool IsChunkProgress { get; set; }
    }
}
