using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;


namespace SimpleNavigator
{
    public partial class MainPage : ContentPage
    {
        private Location targetLocation = null;
        private bool isCompassActive = false;
        private IDispatcherTimer gpsTimer;
        private bool isUpdatingLocation = false;
        private CancellationTokenSource gpsCancellationTokenSource;
        private Location lastKnownLocation;
 
        public MainPage()
        {
            InitializeComponent();
            Compass.ReadingChanged += OnCompassChanged;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartGpsUpdates();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopGpsUpdates();
            Compass.Stop();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            StopGpsUpdates();
            Compass.Stop();
            Application.Current.Quit();
        }

        private async Task UpdateGpsLocation()
        {
            if (isUpdatingLocation) return;

            isUpdatingLocation = true;
            try
            {
                gpsCancellationTokenSource = new CancellationTokenSource();
                var request = new GeolocationRequest(GeolocationAccuracy.Medium); // can change to High for better accuracy but more battery consumption 

                var location = await Geolocation.Default.GetLocationAsync(request, gpsCancellationTokenSource.Token);

                if (location != null)
                {
                    double distance = 0;
                    if (lastKnownLocation != null)
                    {
                        distance = location.CalculateDistance(lastKnownLocation, DistanceUnits.Kilometers);

                    }

                    lastKnownLocation = location;
                    GpsLabel.Text = $"Current GPS : {location.Latitude:F6}, {location.Longitude:F6}";

                    if (distance > 5) // update frequency based on distance / time
                    {
                        gpsTimer.Interval = TimeSpan.FromSeconds(1);
                    }
                    else
                    {
                        gpsTimer.Interval = TimeSpan.FromSeconds(5);
                    }
                }
                else
                {
                    GpsLabel.Text = "GPS: Unavailable (pls wait...)";
                }
            }
            catch (OperationCanceledException)
            {
                GpsLabel.Text = "GPS: Update canceled";
            }
            catch (Exception ex)
            {
                GpsLabel.Text = $"GPS: Unavailable ({ex.Message})";
                Console.WriteLine($"GPS Error: {ex}");
            }
            finally
            {
                isUpdatingLocation = false;
                gpsCancellationTokenSource?.Dispose();
                gpsCancellationTokenSource = null;
            }
        }

        private void OnResetClicked(object sender, EventArgs e)
        {
            TargetPosition = null;
            TargetLocationLabel.Text = "Target GPS: N/A";

            if (isCompassActive)
            {
                Compass.Stop();
                isCompassActive = false;
                CompassStatusLabel.Text = "Compass: OFF";
                CompassStatusLabel.TextColor = Colors.Red;
            }

            StopGpsUpdates();
            gpsTimer = null;
            StartGpsUpdates();
            ArrowImage.Rotation = 0;
        }

        private void StopGpsUpdates()
        {
            if (gpsTimer != null)
            {
                gpsCancellationTokenSource?.Cancel();
                gpsTimer.Stop();
            }
        }

        private void StartGpsUpdates()
        {
            if (gpsTimer == null)
            {
                gpsTimer = Dispatcher.CreateTimer();
                gpsTimer.Interval = TimeSpan.FromSeconds(5); // Default update frequency in seconds
                gpsTimer.Tick += async (s, e) => await UpdateGpsLocation();
            }

            if (!gpsTimer.IsRunning)
            {
                gpsTimer.Start();
            }
        }

        private async void OnSaveLocationClicked(object sender, EventArgs e)
        {
            try
            {
                var request = new GeolocationRequest(GeolocationAccuracy.High); // Pre uloženie pozície môžeme použiť vyššiu presnosť
                var location = await Geolocation.Default.GetLocationAsync(request);

                if (location != null)
                {
                    targetLocation = location;
                    TargetLocationLabel.Text = $"{location.Latitude:F6}, {location.Longitude:F6}";
                }
                else
                {
                    TargetLocationLabel.Text = "Target GPS: N/A (null)";
                    await DisplayAlert("Error", "Unable to store GPS position, try again...", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error while saving the position: {ex.Message}", "OK");
                Console.WriteLine($"Save Location Error: {ex}");
            }
        }

        private void OnShowDirectionClicked(object sender, EventArgs e)
        {
            if (targetLocation == null)
            {
                DisplayAlert("Error", "First, save your target GPS position!", "OK");
                return;
            }

            isCompassActive = !isCompassActive;

            if (isCompassActive)
            {
                Compass.Start(SensorSpeed.UI);
                CompassStatusLabel.Text = "Compass: ON";
                CompassStatusLabel.TextColor = Colors.Green;
            }
            else
            {
                Compass.Stop();
                CompassStatusLabel.Text = "Compass: OFF";
                CompassStatusLabel.TextColor = Colors.Red;
            }
        }

        private void OnCompassChanged(object sender, CompassChangedEventArgs e)
        {
            if (targetLocation == null || lastKnownLocation == null) return;

            double bearingToTarget = GetBearing(lastKnownLocation, targetLocation);
            double heading = e.Reading.HeadingMagneticNorth;
            double rotation = (bearingToTarget - heading + 360) % 360;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ArrowImage.Rotation = rotation;
            });
        }

        private double GetBearing(Location currentLocation, Location targetLocation)
        {
            double lat1 = DegreesToRadians(currentLocation.Latitude);
            double lon1 = DegreesToRadians(currentLocation.Longitude);
            double lat2 = DegreesToRadians(targetLocation.Latitude);
            double lon2 = DegreesToRadians(targetLocation.Longitude);

            double deltaLon = lon2 - lon1;
            double x = Math.Sin(deltaLon) * Math.Cos(lat2);
            double y = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
            double bearing = Math.Atan2(x, y);

            return (RadiansToDegrees(bearing) + 360) % 360;
        }

        private double DegreesToRadians(double degrees) => degrees * (Math.PI / 180);
        private double RadiansToDegrees(double radians) => radians * (180 / Math.PI);
    }
}