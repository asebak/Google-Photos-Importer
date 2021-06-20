using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Google.Apis.Services;
using Newtonsoft.Json;

namespace exporter
{
    internal class PhotosService : Google.Apis.Services.BaseClientService
    {
        private BaseClientService.Initializer initializer;
        private PhotosResource resource;
        public PhotosService(Initializer initializer) : base(initializer)
        {
            this.initializer = initializer;
            this.resource  = new PhotosResource(this);

        }

        public override string Name => "photos";

        public override string BaseUri => "https://photoslibrary.googleapis.com/";

        public override string BasePath => "";

        public override IList<string> Features => new string[0];

        private async Task<List<MediaItem>> CreateHeirachy()
        {
            var directoryInfo = PrepareDirectory();
            var directories = directoryInfo.GetDirectories();
            var existingAlbumListComputerGenerated = await GetAlbumsAsync();
            foreach (var d in directories)
            {
                var match = existingAlbumListComputerGenerated.FirstOrDefault(stringToCheck => stringToCheck.title.Contains(d.Name));
                if (match == null)
                {
                    var result = await this.resource.CreateAlbumAsync(new RootAlbum
                    {
                        album = new Album
                        {
                            title = d.Name,
                            id = Guid.NewGuid().ToString()
                        }
                    });
                    if(result != null)
                    {
                        UploadFolderContentsAsync(result, d);
                    }
                }
                else
                {
                    UploadFolderContentsAsync(match, d);
                }
            }
            return new List<MediaItem>();
        }

        private void UploadFolderContentsAsync(Album album, DirectoryInfo dire)
        {
            var files = dire.GetFiles().ToList();
        }

        private async Task<List<Album>> GetAlbumsAsync()
        {
            var results = new AlbumList { Albums = new List<Album>() };
            var data = await resource.GetAlbumListAsync();
            if (data.Albums != null)
            {
                results.Albums.AddRange(data.Albums);
            }
            while (!string.IsNullOrEmpty(data.NextPageToken))
            {
                data = await resource.GetAlbumListAsync(data.NextPageToken);
                if (data.Albums != null)
                {
                    results.Albums.AddRange(data.Albums);
                }
            }

            return results.Albums;
        }

        public async Task ImportMediaAsync()
        {
            var mediaList = await CreateHeirachy();

            foreach (var item in mediaList)
            {
               // UploadMedia(item, directory);
            }
        }

 

        private DirectoryInfo PrepareDirectory()
        {
           var uploadsFolder = Environment.CurrentDirectory;
           var directoryInfo = System.IO.Directory.CreateDirectory(uploadsFolder);
           return directoryInfo;
        }


        internal class PhotosResource
        {
            private readonly Google.Apis.Services.IClientService service;

            /// <summary>Constructs a new resource.</summary>
            public PhotosResource(Google.Apis.Services.IClientService service)
            {
                this.service = service;
            }

            public async Task<AlbumList> GetAlbumListAsync(string pageToken = "")
            {

                //https://photoslibrary.googleapis.com/v1/albums
                var result = new HttpResponseMessage();
                if (string.IsNullOrEmpty(pageToken))
                {
                    result = await this.service.HttpClient.GetAsync(new Uri($"{service.BaseUri}/v1/albums?pageSize=50&excludeNonAppCreatedData=true"));
                }
                else
                {
                    result = await this.service.HttpClient.GetAsync(new Uri($"{service.BaseUri}/v1/albums?pageSize=50&pageToken={pageToken}&excludeNonAppCreatedData=true"));
                }

                var data = JsonConvert.DeserializeObject<AlbumList>(result.Content.ReadAsStringAsync().Result);
                return data;
            }

            public async Task<Album> CreateAlbumAsync(RootAlbum album)
            {
                var content = new StringContent(JsonConvert.SerializeObject(album));
                var result = await this.service.HttpClient.PostAsync(new Uri($"{service.BaseUri}/v1/albums"), content);
                var data = JsonConvert.DeserializeObject<Album>(result.Content.ReadAsStringAsync().Result);
                return data;
            }

        }
        public class Photo
        {
            [JsonProperty("cameraMake")]
            public string CameraMake { get; set; }

            [JsonProperty("cameraModel")]
            public string CameraModel { get; set; }

            [JsonProperty("focalLength")]
            public double FocalLength { get; set; }

            [JsonProperty("apertureFNumber")]
            public double ApertureFNumber { get; set; }

            [JsonProperty("isoEquivalent")]
            public int IsoEquivalent { get; set; }
        }

        public class MediaMetadata
        {
            [JsonProperty("creationTime")]
            public DateTime CreationTime { get; set; }

            [JsonProperty("width")]
            public string Width { get; set; }

            [JsonProperty("height")]
            public string Height { get; set; }

            [JsonProperty("photo")]
            public Photo Photo { get; set; }
        }

        public class MediaItem
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("productUrl")]
            public string ProductUrl { get; set; }

            [JsonProperty("baseUrl")]
            public string BaseUrl { get; set; }

            [JsonProperty("mimeType")]
            public string MimeType { get; set; }

            [JsonProperty("mediaMetadata")]
            public MediaMetadata MediaMetadata { get; set; }

            [JsonProperty("filename")]
            public string Filename { get; set; }
        }

        public class MediaList
        {
            [JsonProperty("mediaItems")]
            public List<MediaItem> MediaItems { get; set; }

            [JsonProperty("nextPageToken")]
            public string NextPageToken { get; set; }
        }

        public class AlbumList
        {
            [JsonProperty("albums")]
            public List<Album> Albums { get; set; }

            [JsonProperty("nextPageToken")]
            public string NextPageToken { get; set; }
        }

        public class SharedAlbumOptions
        {
            public bool isCollaborative { get; set; }
            public bool isCommentable { get; set; }
        }

        public class ShareInfo
        {
            public SharedAlbumOptions sharedAlbumOptions { get; set; }
            public string shareableUrl { get; set; }
            public string shareToken { get; set; }
            public bool isJoined { get; set; }
            public bool isOwned { get; set; }
            public bool isJoinable { get; set; }
        }

        public class Album
        {
            public string id { get; set; }
            public string title { get; set; }
            public string productUrl { get; set; }
            public bool isWriteable { get; set; }
          public ShareInfo shareInfo { get; set; }
            public string mediaItemsCount { get; set; }
            public string coverPhotoBaseUrl { get; set; }
            public string coverPhotoMediaItemId { get; set; }
        }

        public class RootAlbum
        {
            public Album album { get; set; }
        }
    }
}