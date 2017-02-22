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
using DarkRoomSelfie.Helpers;
using Windows.UI.Xaml.Media;
using Windows.ApplicationModel.Core;

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
        private bool _externalCamera;
        private bool _mirroringPreview;
        private bool _isPreviewing;

        private CameraRotationHelper _rotationHelper;


        private async Task InitializeCameraAsync()
        {
            if (_mediaCapture == null)
            {
                try
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

                    if (preferredDevice.EnclosureLocation == null || preferredDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        _externalCamera = true;
                    }
                    else
                    {
                        _externalCamera = false;
                        _mirroringPreview = (preferredDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
                    }

                    _mirroringPreview = true;

                    // Create MediaCapture
                    _mediaCapture = new MediaCapture();

                    // Initialize MediaCapture and settings
                    await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
                    {
                        VideoDeviceId = preferredDevice.Id
                    });

                    _rotationHelper = new CameraRotationHelper(preferredDevice.EnclosureLocation);
                    _rotationHelper.OrientationChanged += RotationHelper_OrientationChanged;

                    // Set the preview source for the CaptureElement
                    PreviewControl.Source = _mediaCapture;
                    PreviewControl.FlowDirection = _mirroringPreview ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

                    // Start viewing through the CaptureElement 
                    await _mediaCapture.StartPreviewAsync();
                    await SetPreviewRotationAsync();
                }
                catch (UnauthorizedAccessException)
                {
                    // This will be thrown if the user denied access to the camera in privacy settings
                    System.Diagnostics.Debug.WriteLine("This app was denied access to the camera");
                    var dialog = new Windows.UI.Popups.MessageDialog("This app was denied access to the camera");
                    await dialog.ShowAsync();
                    CoreApplication.Exit();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("MediaCapture initialization failed. {0}", ex.Message);
                    var dialog = new Windows.UI.Popups.MessageDialog("MediaCapture initialization failed. { 0 }", ex.Message);
                    await dialog.ShowAsync();
                    CoreApplication.Exit();
                }
            }
        }

        private async Task SetPreviewRotationAsync()
        {
            if (!_externalCamera)
            {
                // Add rotation metadata to the preview stream to make sure the aspect ratio / dimensions match when rendering and getting preview frames
                var rotation = _rotationHelper.GetCameraPreviewOrientation();
                var props = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
                Guid RotationKey = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");
                props.Properties.Add(RotationKey, CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(rotation));
                await _mediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, null);
            }
        }

        private async void RotationHelper_OrientationChanged(object sender, bool updatePreview)
        {
            if (updatePreview)
            {
                await SetPreviewRotationAsync();
            }
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                // Rotate the buttons in the UI to match the rotation of the device
                var angle = CameraRotationHelper.ConvertSimpleOrientationToClockwiseDegrees(_rotationHelper.GetUIOrientation());
                var transform = new RotateTransform { Angle = angle };

                // The RenderTransform is safe to use (i.e. it won't cause layout issues) in this case, because these buttons have a 1:1 aspect ratio
                Capture.RenderTransform = transform;
                Capture.RenderTransform = transform;
            });
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

                    //Getting the current orientation of the device
                    var photoOrientation = CameraRotationHelper.ConvertSimpleOrientationToPhotoOrientation(_rotationHelper.GetCameraCaptureOrientation());

                    //Including metadata about the photo in the image file
                    var properties = new BitmapPropertySet
                    {
                        { "System.Photo.Orientation", new BitmapTypedValue(photoOrientation, PropertyType.UInt16) }
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
