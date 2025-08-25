using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

namespace SimpleNavigator
{
    // Pomocná trieda pre JSON serializáciu
    public class LocationData
    {
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    // Model pre uložené lokácie
    public class SavedLocation : INotifyPropertyChanged
    {
        private string _name;
        private double _latitude;
        private double _longitude;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public double Latitude
        {
            get => _latitude;
            set
            {
                _latitude = value;
                OnPropertyChanged(nameof(Latitude));
            }
        }

        public double Longitude
        {
            get => _longitude;
            set
            {
                _longitude = value;
                OnPropertyChanged(nameof(Longitude));
            }
        }

        public string DisplayText => $"{Name} ({Latitude:F6}, {Longitude:F6})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainPage : ContentPage
    {
        private Location targetLocation = null;
        private bool isCompassActive = false;
        private IDispatcherTimer gpsTimer;
        private bool isUpdatingLocation = false;
        private CancellationTokenSource gpsCancellationTokenSource;
        private Location lastKnownLocation;
        private ObservableCollection<SavedLocation> savedLocations;
        private SavedLocation selectedLocation;

        public MainPage()
        {
            InitializeComponent();
            Compass.ReadingChanged += OnCompassChanged;

            // Inicializácia zoznamu uložených lokácií
            savedLocations = new ObservableCollection<SavedLocation>();
            SavedLocationsPicker.ItemsSource = savedLocations;

            // Načítanie lokácií zo storage pri štarte
            LoadSavedLocations();

            // Skryť zoznam na začiatku ak je prázdny
            UpdateLocationListVisibility();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            StartGpsUpdates();

            // Debug info o počte lokácií
            Console.WriteLine($"OnAppearing: {savedLocations.Count} locations loaded");
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopGpsUpdates();
            if (isCompassActive)
            {
                Compass.Stop();
                isCompassActive = false;
            }

            // Uloženie lokácií pred zatvorením
            SaveLocationsToStorage();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            StopGpsUpdates();
            if (isCompassActive)
            {
                Compass.Stop();
                isCompassActive = false;
            }

            // Uloženie lokácií pred ukončením
            SaveLocationsToStorage();
            Application.Current.Quit();
        }

        private async Task UpdateGpsLocation()
        {
            if (isUpdatingLocation) return;

            isUpdatingLocation = true;
            try
            {
                gpsCancellationTokenSource = new CancellationTokenSource();
                var request = new GeolocationRequest(GeolocationAccuracy.Medium);

                var location = await Geolocation.Default.GetLocationAsync(request, gpsCancellationTokenSource.Token);

                if (location != null)
                {
                    double distance = 0;
                    if (lastKnownLocation != null)
                    {
                        distance = location.CalculateDistance(lastKnownLocation, DistanceUnits.Kilometers);
                    }

                    lastKnownLocation = location;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = $"Current GPS: {location.Latitude:F6}, {location.Longitude:F6}";
                        UpdateDistanceDisplay();
                    });

                    if (distance > 0.005)
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
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = "GPS: Unavailable (pls wait...)";
                    });
                }
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = "GPS: Update canceled";
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = $"GPS: Unavailable ({ex.Message})";
                });
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
            targetLocation = null;
            selectedLocation = null;
            TargetLocationLabel.Text = "Target GPS: N/A";
            DistanceLabel.Text = "Distance: N/A";
            SavedLocationsPicker.SelectedItem = null;

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
                gpsTimer.Interval = TimeSpan.FromSeconds(5);
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
                var request = new GeolocationRequest(GeolocationAccuracy.High);
                var location = await Geolocation.Default.GetLocationAsync(request);

                if (location != null)
                {
                    // Zobrazenie dialógu pre zadanie názvu
                    string name = await DisplayPromptAsync("Save Location", "Enter name for this location:", "Save", "Cancel", "My Location");

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        var savedLocation = new SavedLocation
                        {
                            Name = name.Trim(),
                            Latitude = location.Latitude,
                            Longitude = location.Longitude
                        };

                        savedLocations.Add(savedLocation);
                        UpdateLocationListVisibility();

                        await DisplayAlert("Success", $"Location '{name}' saved successfully!", "OK");
                    }
                }
                else
                {
                    await DisplayAlert("Error", "Unable to get current GPS position, try again...", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error while saving the location: {ex.Message}", "OK");
                Console.WriteLine($"Save Location Error: {ex}");
            }
        }

        private async void OnAddManualLocationClicked(object sender, EventArgs e)
        {
            try
            {
                string name = await DisplayPromptAsync("Add Location", "Enter name for this location:", "Next", "Cancel");
                if (string.IsNullOrWhiteSpace(name)) return;

                string latString = await DisplayPromptAsync("Add Location", "Enter latitude:", "Next", "Cancel", "", -1, Keyboard.Default);
                if (string.IsNullOrWhiteSpace(latString)) return;

                string lonString = await DisplayPromptAsync("Add Location", "Enter longitude:", "Save", "Cancel", "", -1, Keyboard.Default);
                if (string.IsNullOrWhiteSpace(lonString)) return;

                if (TryParseCoordinate(latString, out double lat) && TryParseCoordinate(lonString, out double lon))
                {
                    if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                    {
                        var savedLocation = new SavedLocation
                        {
                            Name = name.Trim(),
                            Latitude = lat,
                            Longitude = lon
                        };

                        savedLocations.Add(savedLocation);
                        UpdateLocationListVisibility();

                        await DisplayAlert("Success", $"Location '{name}' added successfully!", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Error", "Invalid coordinates! Latitude must be between -90 and 90, longitude between -180 and 180.", "OK");
                    }
                }
                else
                {
                    await DisplayAlert("Error", "Invalid number format! Please enter valid coordinates.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error while adding location: {ex.Message}", "OK");
            }
        }

        private void OnLocationSelected(object sender, EventArgs e)
        {
            var picker = sender as Picker;
            if (picker.SelectedItem is SavedLocation location)
            {
                selectedLocation = location;
                targetLocation = new Location(location.Latitude, location.Longitude);
                TargetLocationLabel.Text = $"{location.Latitude:F6}, {location.Longitude:F6}";
                UpdateDistanceDisplay();
            }
        }

        private async void OnEditLocationClicked(object sender, EventArgs e)
        {
            if (selectedLocation == null)
            {
                await DisplayAlert("Error", "Please select a location from the list first!", "OK");
                return;
            }

            string action = await DisplayActionSheet($"Edit '{selectedLocation.Name}'", "Cancel", "Delete", "Rename", "Change Coordinates");

            switch (action)
            {
                case "Rename":
                    await RenameLocation(selectedLocation);
                    break;
                case "Change Coordinates":
                    await ChangeCoordinates(selectedLocation);
                    break;
                case "Delete":
                    await DeleteLocation(selectedLocation);
                    break;
            }
        }

        private async Task RenameLocation(SavedLocation location)
        {
            string newName = await DisplayPromptAsync("Rename Location", "Enter new name:", "Save", "Cancel", location.Name);
            if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != location.Name)
            {
                location.Name = newName.Trim();
                SaveLocationsToStorage(); // Uložiť zmeny
                await DisplayAlert("Success", "Location renamed successfully!", "OK");
            }
        }

        private async Task ChangeCoordinates(SavedLocation location)
        {
            string latString = await DisplayPromptAsync("Change Coordinates", "Enter new latitude:", "Next", "Cancel", location.Latitude.ToString("F6"), -1, Keyboard.Default);
            if (string.IsNullOrWhiteSpace(latString)) return;

            string lonString = await DisplayPromptAsync("Change Coordinates", "Enter new longitude:", "Save", "Cancel", location.Longitude.ToString("F6"), -1, Keyboard.Default);
            if (string.IsNullOrWhiteSpace(lonString)) return;

            if (TryParseCoordinate(latString, out double lat) && TryParseCoordinate(lonString, out double lon))
            {
                if (lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180)
                {
                    location.Latitude = lat;
                    location.Longitude = lon;

                    // Aktualizovať cieľovú lokáciu ak je práve vybraná
                    if (selectedLocation == location)
                    {
                        targetLocation = new Location(lat, lon);
                        TargetLocationLabel.Text = $"{lat:F6}, {lon:F6}";
                        UpdateDistanceDisplay();
                    }

                    await DisplayAlert("Success", "Coordinates updated successfully!", "OK");
                }
                else
                {
                    await DisplayAlert("Error", "Invalid coordinates!", "OK");
                }
            }
            else
            {
                await DisplayAlert("Error", "Invalid number format!", "OK");
            }
        }

        private async Task DeleteLocation(SavedLocation location)
        {
            bool confirm = await DisplayAlert("Delete Location", $"Are you sure you want to delete '{location.Name}'?", "Delete", "Cancel");
            if (confirm)
            {
                savedLocations.Remove(location);

                if (selectedLocation == location)
                {
                    selectedLocation = null;
                    targetLocation = null;
                    TargetLocationLabel.Text = "Target GPS: N/A";
                    DistanceLabel.Text = "Distance: N/A";
                    SavedLocationsPicker.SelectedItem = null;
                }

                UpdateLocationListVisibility();
                SaveLocationsToStorage(); // Uložiť zmeny
                await DisplayAlert("Success", "Location deleted successfully!", "OK");
            }
        }

        private void UpdateLocationListVisibility()
        {
            bool hasLocations = savedLocations.Count > 0;
            SavedLocationsPicker.IsVisible = hasLocations;
            EditLocationButton.IsVisible = hasLocations;

            if (!hasLocations)
            {
                SavedLocationsPicker.SelectedItem = null;
                selectedLocation = null;
            }
        }

        private void OnShowDirectionClicked(object sender, EventArgs e)
        {
            if (targetLocation == null)
            {
                DisplayAlert("Error", "First, select a target location!", "OK");
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
                UpdateDistanceDisplay();
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

        // Pomocná metóda pre parsovanie súradníc - podporuje bodku aj čiarku
        private bool TryParseCoordinate(string input, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Pokus s aktuálnou kultúrou (čiarka v SK)
            if (double.TryParse(input, out result))
                return true;

            // Pokus s invariant kultúrou (bodka)
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            // Manuálna náhrada čiarky za bodku a opätovný pokus
            string normalizedInput = input.Replace(',', '.');
            if (double.TryParse(normalizedInput, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return true;

            return false;
        }

        // Uloženie lokácií do Preferences (trvalé úložisko)
        private void SaveLocationsToStorage()
        {
            try
            {
                var locationList = savedLocations.Select(loc => new LocationData
                {
                    Name = loc.Name,
                    Latitude = loc.Latitude,
                    Longitude = loc.Longitude
                }).ToList();

                string json = JsonSerializer.Serialize(locationList);
                Preferences.Default.Set("SavedLocations", json);

                // Overenie že sa uložilo
                string verification = Preferences.Default.Get("SavedLocations", "");
                bool success = verification == json;

                // Debug výpis
                Console.WriteLine($"Saving {locationList.Count} locations to storage");
                Console.WriteLine($"JSON length: {json.Length}");
                Console.WriteLine($"Verification: {(success ? "SUCCESS" : "FAILED")}");
                Console.WriteLine($"JSON: {json}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving locations: {ex.Message}");
            }
        }

        // Načítanie lokácií z Preferences
        private void LoadSavedLocations()
        {
            try
            {
                string json = Preferences.Default.Get("SavedLocations", "");
                Console.WriteLine($"LoadSavedLocations called");
                Console.WriteLine($"JSON from storage: '{json}'");
                Console.WriteLine($"JSON length: {json.Length}");

                if (!string.IsNullOrEmpty(json))
                {
                    var locationList = JsonSerializer.Deserialize<List<LocationData>>(json);
                    if (locationList != null)
                    {
                        Console.WriteLine($"Deserializing {locationList.Count} locations");

                        savedLocations.Clear(); // Vyčistiť pred načítaním
                        foreach (var item in locationList)
                        {
                            var savedLocation = new SavedLocation
                            {
                                Name = item.Name,
                                Latitude = item.Latitude,
                                Longitude = item.Longitude
                            };
                            savedLocations.Add(savedLocation);
                            Console.WriteLine($"Loaded location: {item.Name} ({item.Latitude}, {item.Longitude})");
                        }

                        Console.WriteLine($"Total locations in collection: {savedLocations.Count}");
                    }
                    else
                    {
                        Console.WriteLine("Deserialization returned null");
                    }
                }
                else
                {
                    Console.WriteLine("No JSON data found in storage");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading locations: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // V prípade chyby vyčistiť poškodené dáta
                Preferences.Default.Remove("SavedLocations");
            }
        }

        private void UpdateDistanceDisplay()
        {
            if (targetLocation == null || lastKnownLocation == null)
            {
                DistanceLabel.Text = "Distance: N/A";
                return;
            }

            double distanceKm = lastKnownLocation.CalculateDistance(targetLocation, DistanceUnits.Kilometers);

            if (distanceKm < 1.0)
            {
                double distanceM = distanceKm * 1000;
                DistanceLabel.Text = $"Distance: {distanceM:F0} m";

                if (distanceM < 10)
                    DistanceLabel.TextColor = Colors.LimeGreen;
                else if (distanceM < 50)
                    DistanceLabel.TextColor = Colors.Yellow;
                else
                    DistanceLabel.TextColor = Colors.Orange;
            }
            else
            {
                DistanceLabel.Text = $"Distance: {distanceKm:F2} km";
                DistanceLabel.TextColor = Colors.Red;
            }
        }
    }
}