using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Media.Capture;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using Windows.System.Display;
using Windows.Graphics.Display;
using Windows.UI.Core;
using Windows.Devices.Enumeration;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using Windows.Storage.FileProperties;
using Windows.Foundation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DarkRoomSelfie
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture = null;
        private DisplayRequest _displayRequest = null;
        private bool _isPreviewing;

        private async Task InitializeCameraAsync()
        {
            if (_mediaCapture == null)
            {
                // Get the camera devices
                var cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

                // try to get the front facing device for a phone
                var FrontFacingDevice = cameraDevices.FirstOrDefault
                (
                    c => c.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Front
                );


                // but if that doesn't exist, take the first camera device available
                var preferredDevice = FrontFacingDevice ?? cameraDevices.FirstOrDefault();

                // Create MediaCapture
                _mediaCapture = new MediaCapture();

                // Initialize MediaCapture and settings
                await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                        VideoDeviceId = preferredDevice.Id
                });

                // Set the preview source for the CaptureElement
                PreviewControl.Source = _mediaCapture;

                // Start viewing through the CaptureElement 
                await _mediaCapture.StartPreviewAsync();
            }
        }

        private async Task StartPreviewAsync()
        {
            try
            {
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync();

                PreviewControl.Source = _mediaCapture;
                await _mediaCapture.StartPreviewAsync();
                _isPreviewing = true;

                _displayRequest.RequestActive();
                DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
            }
            catch (UnauthorizedAccessException)
            {
                // This will be thrown if the user denied access to the camera in privacy settings
                System.Diagnostics.Debug.WriteLine("This app was denied access to the camera");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MediaCapture initialization failed. {0}", ex.Message);
            }
        }
        private async Task CleanupCameraAsync()
        {
            if (_mediaCapture != null)
            {
                if (_isPreviewing)
                {
                    await _mediaCapture.StopPreviewAsync();
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    PreviewControl.Source = null;
                    if (_displayRequest != null)
                    {
                        _displayRequest.RequestRelease();
                    }

                    _mediaCapture.Dispose();
                    _mediaCapture = null;
                });
            }

        }

        //This method is used to call StartPreviewAsync because MainPage cannot have async in its signature
        public async void OpenCamera()
        {
            await StartPreviewAsync();
        }

        public MainPage()
        {
            this.InitializeComponent();

            //OpenCamera();

            //shutting down the preview stream when app suspending
            Application.Current.Suspending += Application_Suspending;

            Application.Current.Resuming += Application_Resuming;
        }

        public async void CapturePhoto_Click(object sender, RoutedEventArgs e)
        {
            //Getting access to Pictures folder
            var myPictures = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);

            //Createing the file that will be saved
            StorageFile file = await myPictures.SaveFolder.CreateFileAsync("night-photo.jpg", CreationCollisionOption.GenerateUniqueName);

            //Capture a photo to the stream
            using (var captureStream = new InMemoryRandomAccessStream())
            {
                //Common encoding with JPEG format
                await _mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), captureStream);

                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    //Decode the image from the memory stream
                    var decoder = await BitmapDecoder.CreateAsync(captureStream);

                    //Encode the image to file
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(fileStream, decoder);

                    //Including metadata about the photo in the image file
                    var properties = new BitmapPropertySet
                    {
                        { "System.Photo.Orientation", new BitmapTypedValue(PhotoOrientation.Normal, PropertyType.UInt16) }
                    };
                    await encoder.BitmapProperties.SetPropertiesAsync(properties);

                    await encoder.FlushAsync();
                }
            }   
        }

        private async void Application_Resuming(object sender, object o)
        {
            await InitializeCameraAsync();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            await InitializeCameraAsync();
        }
        //shutting down the preview stream when the user navigates away from our page
        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            await CleanupCameraAsync();
        }

        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await CleanupCameraAsync();
                deferral.Complete();
            }
        }
    }
}
