using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Google.Apis.Services;
using Newtonsoft.Json;
using MimeTypes;

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

        static string[] mediaExtensions = {
    ".PNG", ".JPG", ".JPEG", ".BMP", ".GIF", 
    ".WAV", ".MID", ".MIDI", ".WMA", ".MP3", ".OGG", ".RMA", 
    ".AVI", ".MP4", ".DIVX", ".WMV" };


    static bool IsMediaFile(string path)
        {
            return -1 != Array.IndexOf(mediaExtensions, Path.GetExtension(path).ToUpperInvariant());
        }
        public override string Name => "photos";

        public override string BaseUri => "https://photoslibrary.googleapis.com/";

        public override string BasePath => "";

        public override IList<string> Features => new string[0];

        private async Task<List<MediaItem>> CreateHeirachy()
        {
            var directoryInfo = PrepareDirectory();
            UploadFolderContentsAsync(null, directoryInfo);
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
                        Console.WriteLine($"Created new album: {result.title}");
                        UploadFolderContentsAsync(result, d);
                    }
                }
                else
                {
                    Console.WriteLine($"Using existing album: {match.title}");
                    UploadFolderContentsAsync(match, d);
                }
            }
            return new List<MediaItem>();
        }

        private void UploadFolderContentsAsync(Album album, DirectoryInfo dire)
        {
            var files = dire.GetFiles().ToList();
            //todo get media items in album, then check if it already exists, if it does skip it to optimize performance. 
            MediaList mediaItems = new MediaList
            {
                MediaItems = new List<MediaItem>()
            };

            if(album != null)
            {
                mediaItems = this.resource.GetMediaByAlbumAsync(album.id).Result;
            }

            foreach (var f in files)
            {
                var match = mediaItems.MediaItems.FirstOrDefault(stringToCheck => stringToCheck.Filename.Contains(f.Name));
                if (match != null)
                {
                    continue;
                }
                    var data =  this.resource.UploadBytesAsync(f).Result;
                if(!string.IsNullOrEmpty(data))
                {
                    //upload directly 
                    var result = this.resource.BatchCreateAsync(new NewMediaList
                    {
                        AlbumId = album == null ? string.Empty : album.id,
                        NewMediaItems = new List<NewMediaItem>
                            {
                                new NewMediaItem
                                {
                                    simpleMediaItem = new SimpleMediaItem
                                    {
                                        uploadToken = data,
                                        fileName = f.Name
                                    },
                                    description = string.Empty
                                }
                            }
                    }).Result;

                    if(result != null && album != null) 
                    {
                        var addedToAlbum = this.resource.BatchAddMediaItemsAsync(album.id, new MediaItemIds
                        {
                            Ids = result.newMediaItemResults.Select(x => x.mediaItem.Id).ToList()
                        }).Result;
                    }
                }
            }

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
             await CreateHeirachy();
            Console.WriteLine("Completed Upload.");
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

            public async Task<MediaList> GetMediaByAlbumAsync(string albumId)
            {
                var maxPageSizeCount = "100";
                var result = new HttpResponseMessage();
                var content = new StringContent(JsonConvert.SerializeObject(new MediaSearch { 
                AlbumId = albumId,
                PageSize = maxPageSizeCount
                }));
                result = await this.service.HttpClient.PostAsync(new Uri($"{service.BaseUri}/v1/mediaItems:search"), content);


                var data = JsonConvert.DeserializeObject<MediaList>(result.Content.ReadAsStringAsync().Result);
                return data;
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


            //uploads data
            public async Task<string> UploadBytesAsync(FileInfo file)
            {
                if(!IsMediaFile(file.Name))
                {
                    return string.Empty;
                }
                Console.WriteLine($"Uploading: {file.Name}");
            
                var filebytes = File.ReadAllBytes(file.FullName);

                using (var content = new ByteArrayContent(filebytes))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Headers.Add("X-Goog-Upload-Content-Type", MimeTypeMap.GetMimeType(file.Extension)); //todo fill this in via library from the filename
                    content.Headers.Add("X-Goog-Upload-Protocol", "raw");
                    var result = await this.service.HttpClient.PostAsync(new Uri($"{service.BaseUri}/v1/uploads"), content);
                    var uploadToken = await result.Content.ReadAsStringAsync();
                    return uploadToken;
                }

            }


            //creates media item
            public async Task<NewMediaItemResultRoot> BatchCreateAsync(NewMediaList newMediaList)
            {

                var maxBatchSize = 50;
                for (int i = 0; i < newMediaList.NewMediaItems.Count; i += maxBatchSize)
                {
                    var list = new NewMediaList {
                        NewMediaItems = new List<NewMediaItem>(),
                        AlbumId = newMediaList.AlbumId
                    };

                    list.NewMediaItems.AddRange(newMediaList.NewMediaItems.GetRange(i, Math.Min(maxBatchSize, newMediaList.NewMediaItems.Count - i)));


                    var content = new StringContent(JsonConvert.SerializeObject(newMediaList));
                    var result = await this.service.HttpClient.PostAsync(new Uri($"{service.BaseUri}/v1/mediaItems:batchCreate"), content);
                    var data = JsonConvert.DeserializeObject<NewMediaItemResultRoot>(result.Content.ReadAsStringAsync().Result);
                    return data;
                }

                return null;
            }

            //batches media items to an album
            public async Task<bool> BatchAddMediaItemsAsync(string albumId, MediaItemIds mediaItemIds)
            {
                var maxBatchSize = 50;
                for (int i = 0; i < mediaItemIds.Ids.Count; i += maxBatchSize)
                {
                    var list = new MediaItemIds
                    {
                        Ids = new List<string>()
                    };

                    list.Ids.AddRange(mediaItemIds.Ids.GetRange(i, Math.Min(maxBatchSize, mediaItemIds.Ids.Count - i)));

                    var content = new StringContent(JsonConvert.SerializeObject(mediaItemIds));
                    var result = await this.service.HttpClient.PostAsync(new Uri($"{service.BaseUri}/v1/albums/{albumId}:batchAddMediaItems"), content);
                    var data = JsonConvert.DeserializeObject<Album>(result.Content.ReadAsStringAsync().Result);
                }

                return true;
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

        public class NewMediaList
        {
            [JsonProperty("newMediaItems")]
            public List<NewMediaItem> NewMediaItems { get; set; }

            [JsonProperty("albumId")]
            public string AlbumId { get; set; }
        }

        public class MediaSearch
        {
            [JsonProperty("pageSize")]
            public string PageSize { get; set; }

            [JsonProperty("albumId")]
            public string AlbumId { get; set; }
        }

        public class SimpleMediaItem
        {
            public string fileName { get; set; }
            public string uploadToken { get; set; }
        }

        public class NewMediaItem
        {
            public string description { get; set; }
            public SimpleMediaItem simpleMediaItem { get; set; }
        }

        public class MediaItemIds
        {
            [JsonProperty("mediaItemIds")]
            public List<string> Ids { get; set; }

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



        public class Status
        {
            public string message { get; set; }
            public int? code { get; set; }
        }



        public class NewMediaItemResult
        {
            public string uploadToken { get; set; }
            public Status status { get; set; }
            public MediaItem mediaItem { get; set; }
        }

        public class NewMediaItemResultRoot
        {
            public List<NewMediaItemResult> newMediaItemResults { get; set; }
        }

    }
}