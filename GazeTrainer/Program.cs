using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Media;
using Tobii.StreamEngine;
using System.Threading.Tasks;


namespace GazeTrainer
{
    public static class GazeTrainer
    {
        /*****************************
        Display Settings
            Defines the size of your monitor so we can compare the boundry box settings to tobii's screen coordinates
        ******************************/
        // The width of your display
        private static readonly float DisplayWidth = 3840;
        // The height of your display
        private static readonly float DisplayHeight = 2160;

        /*****************************
        Boundry Box Settings
            Defines the area of your screen where it is safe to look
        *****************************/
        // The the distance from the left of your display to the left side of the boundry box
        private static float BoundryBoxX = 0;
        // The the distance from the top of your display to the top of the boundry box
        private static float BoundryBoxY = 0;
        // The width of the boundry box
        private static readonly float BoundryBoxWidth = 3840;
        // The height of the boundry box
        private static readonly float BoundryBoxHeight = 2160;
        // If true ignores specified X and Y and places boundry box with specified height and width in the center of the display
        private static readonly bool AutoCenterBoundryBox = false;

        /*****************************
        Punishment Settings
            Defines when punishment is started due to the user looking outside the boundry box
        *****************************/
        // How long does the user need to look outside the boundry box for the punishment SFX start?
        private static readonly int PunishmentThresholdMs = 100;
        // Should invalid data from Tobii API (eg. closed eyes) be ignored or treated as out of bounds?
        private static readonly bool TreatInvalidDataAsOutOfBounds = true;

        /*****************************
        Internal Variables
            You shouldn't need to edit these
        *****************************/
        // Left side of the boundry box
        private static float XBoundsMin;
        // Right side of the boundry box
        private static float XBoundsMax;
        // Top of the boundry box
        private static float YBoundsMin;
        // Bottom of the boundry box
        private static float YBoundsMax;
        // Should program continue running?
        static bool _continue = true;
        // The sound file to play when punishing the user
        static readonly SoundPlayer punishmentFilePlayer = new SoundPlayer(Properties.Resources.punish_loop_wav);
        // Timestamp when the gaze point was last polled
        private static DateTime LastPollTime;
        // Is the punishment sound playing?
        private static bool punishmentPlaying = false;
        // How long has the gaze point been outside the boundry box?
        private static long DistrationTimeMs = 0;

        private static void OnGazePoint(ref tobii_gaze_point_t gazePoint, IntPtr userData)
        {
            // Check that the data is valid before using it
            if (gazePoint.validity == tobii_validity_t.TOBII_VALIDITY_VALID)
            {
                //Console.WriteLine($"Gaze bounds: X[{XBoundsMin}-{XBoundsMax}], Y[{YBoundsMin}-{YBoundsMax}]");
                //Console.WriteLine($"Gaze point: X[{gazePoint.position.x}], Y[{gazePoint.position.y}]");

                if (((XBoundsMin <= gazePoint.position.x) && (gazePoint.position.x <= XBoundsMax))
                    && ((YBoundsMin <= gazePoint.position.y) && (gazePoint.position.y <= YBoundsMax)))
                {
                    HandleGoodGaze();
                }
                else
                {
                    HandleBadGaze();
                }
            }
            else
            {
                // Invalid Data Detected
                if (TreatInvalidDataAsOutOfBounds)
                {
                    HandleBadGaze();
                }
            }

            LastPollTime = DateTime.Now;
        }

        public static void HandleGoodGaze()
        {
            if (punishmentPlaying)
            {
                punishmentFilePlayer.Stop();
                punishmentPlaying = false;
            }

            DistrationTimeMs = 0;
        }

        public static void HandleBadGaze()
        {
            DistrationTimeMs += (long)(DateTime.Now - LastPollTime).TotalMilliseconds;
            if (DistrationTimeMs > PunishmentThresholdMs)
            {
                if (punishmentPlaying == false)
                {
                    //Console.WriteLine("Begin user punishment");
                    punishmentFilePlayer.PlayLooping();
                    punishmentPlaying = true;
                }
            }
        }

        public static void Main()
        {
            // Process bounds
            if (AutoCenterBoundryBox)
            {
                BoundryBoxX = (DisplayWidth - BoundryBoxWidth) / 2;
                BoundryBoxY = (DisplayHeight - BoundryBoxHeight) / 2;
            }
            XBoundsMin = (BoundryBoxX) / DisplayWidth;
            XBoundsMax = (BoundryBoxX + BoundryBoxWidth) / DisplayWidth;
            YBoundsMin = (BoundryBoxY) / DisplayHeight;
            YBoundsMax = (BoundryBoxY + BoundryBoxHeight) / DisplayHeight;

            Console.WriteLine("Bounds configured");
            Console.WriteLine($"Bounds: X[{XBoundsMin}-{XBoundsMax}], Y[{YBoundsMin}-{YBoundsMax}]");

            // TOBII SETUP
            // Create API context
            IntPtr apiContext;
            tobii_error_t result = Interop.tobii_api_create(out apiContext, null);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);

            // Enumerate devices to find connected eye trackers
            List<string> urls;
            result = Interop.tobii_enumerate_local_device_urls(apiContext, out urls);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);
            if (urls.Count == 0)
            {
                Console.WriteLine("Error: No device found");
                return;
            }

            Console.WriteLine("Eyetracker Found");

            // Connect to the first tracker found
            IntPtr deviceContext;
            result = Interop.tobii_device_create(apiContext, urls[0], Interop.tobii_field_of_use_t.TOBII_FIELD_OF_USE_INTERACTIVE, out deviceContext);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);

            Console.WriteLine("Connected to Eyetracker");

            // Subscribe to gaze data
            result = Interop.tobii_gaze_point_subscribe(deviceContext, OnGazePoint);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);

            Console.WriteLine("Subscribed to Gaze Data");

            while (_continue)
            {
                // Optionally block this thread until data is available. Especially useful if running in a separate thread.
                Interop.tobii_wait_for_callbacks(new[] { deviceContext });
                Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR || result == tobii_error_t.TOBII_ERROR_TIMED_OUT);

                // Process callbacks on this thread if data is available
                if (!Object.ReferenceEquals(deviceContext, null))
                {
                    Interop.tobii_device_process_callbacks(deviceContext);
                    Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);
                }
                Task.Delay(100);
            }

            // Cleanup
            _continue = false;
            result = Interop.tobii_gaze_point_unsubscribe(deviceContext);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);
            result = Interop.tobii_device_destroy(deviceContext);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);
            result = Interop.tobii_api_destroy(apiContext);
            Debug.Assert(result == tobii_error_t.TOBII_ERROR_NO_ERROR);
        }
    }
}