﻿using HomeSeerAPI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Unosquare.Swan;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Class to store PlugIn Configuration
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    internal class PluginConfig : IDisposable
    {
        public event EventHandler<EventArgs> ConfigChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        /// <param name="HS">The homeseer application.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1806:DoNotIgnoreMethodResults",
             MessageId = "System.Net.IPAddress.TryParse(System.String,System.Net.IPAddress@)")]
        public PluginConfig(IHSApplication HS)
        {
            this.HS = HS;
            debugLogging = GetValue(DebugLoggingKey, false);
            forwardSpeech = GetValue(ForwardSpeechKey, true);
            sapiVoice = GetValue<string>(SapiVoiceKey, null);

            string webServerIPAddressString = GetValue(WebServerIPAddressKey, string.Empty);
            IPAddress.TryParse(webServerIPAddressString, out webServerIPAddress);

            webServerPort = GetValue<ushort>(WebServerPortKey, 8081);

            string deviceIdsConcatString = GetValue(DeviceIds, string.Empty);
            var deviceIds = deviceIdsConcatString.Split(DeviceIdsSeparator);
            foreach (var deviceId in deviceIds)
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }
                string ipAddressString = GetValue(IPAddressKey, string.Empty, deviceId);
                IPAddress.TryParse(ipAddressString, out var deviceIP);
                string name = GetValue(NameKey, string.Empty, deviceId);
                var volume = GetValue<short>(VolumeKey, -1, deviceId);
                devices.Add(deviceId, new ChromecastDevice(deviceId, name, deviceIP, volume == -1 ? null : (ushort?)volume));
            }
            // Auto create entries in INI File
            WebServerPort = this.webServerPort;
        }

        /// <summary>
        /// Gets or sets the devices
        /// </summary>
        /// <value>
        /// The API key.
        /// </value>
        public IReadOnlyDictionary<string, ChromecastDevice> Devices
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return new Dictionary<string, ChromecastDevice>(devices);
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether debug logging is enabled.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [debug logging]; otherwise, <c>false</c>.
        /// </value>
        public bool DebugLogging
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return debugLogging;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(DebugLoggingKey, value);
                    debugLogging = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public bool ForwardSpeach
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return forwardSpeech;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(ForwardSpeechKey, value);
                    forwardSpeech = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public string SAPIVoice
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return sapiVoice;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(SapiVoiceKey, value);
                    sapiVoice = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public ushort WebServerPort
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return webServerPort;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }
            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(WebServerPortKey, value);
                    webServerPort = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public IPAddress WebServerIPAddress
        {
            get
            {
                configLock.EnterReadLock();
                try
                {
                    return webServerIPAddress;
                }
                finally
                {
                    configLock.ExitReadLock();
                }
            }

            set
            {
                configLock.EnterWriteLock();
                try
                {
                    SetValue(WebServerIPAddressKey, value);
                    webServerIPAddress = value;
                }
                finally
                {
                    configLock.ExitWriteLock();
                }
            }
        }

        public void AddDevice(ChromecastDevice device)
        {
            configLock.EnterWriteLock();
            try
            {
                devices[device.Id] = device;
                SetValue(NameKey, device.Name, device.Id);
                SetValue(IPAddressKey, device.DeviceIP.ToString(), device.Id);
                SetValue(VolumeKey, device.Volume, device.Id);
                SetValue(DeviceIds, devices.Keys.Aggregate((x, y) => x + DeviceIdsSeparator + y));
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public void RemoveDevice(string deviceId)
        {
            configLock.EnterWriteLock();
            try
            {
                devices.Remove(deviceId);
                if (devices.Count > 0)
                {
                    SetValue(DeviceIds, devices.Keys.Aggregate((x, y) => x + DeviceIdsSeparator + y));
                }
                else
                {
                    SetValue(DeviceIds, string.Empty);
                }
                HS.ClearINISection(deviceId, FileName);
            }
            finally
            {
                configLock.ExitWriteLock();
            }
        }

        public IPAddress CalculateServerIPAddress()
        {
            // if there is override address, use it
            var overrideIPAddress = WebServerIPAddress;
            if ((overrideIPAddress != null) && (!overrideIPAddress.Equals(IPAddress.Any)))
            {
                return overrideIPAddress;
            }

            var ipAddresses = Network.GetIPv4Addresses(false);

            var hsAddress = IPAddress.Parse(HS.GetIPAddress());

            // if nothing is specified and hs address is in local addresses, us it
            if (ipAddresses.Contains(hsAddress))
            {
                return hsAddress;
            }

            if (ipAddresses.Length == 0)
            {
                throw new IOException("No Local IP4 Address Found");
            }
            return ipAddresses.First();
        }

        private T GetValue<T>(string key, T defaultValue)
        {
            return GetValue(key, defaultValue, DefaultSection);
        }

        private T GetValue<T>(string key, T defaultValue, string section)
        {
            string stringValue = HS.GetINISetting(section, key, null, FileName);
            if (stringValue != null)
            {
                try
                {
                    T result = (T)System.Convert.ChangeType(stringValue, typeof(T), CultureInfo.InvariantCulture);
                    return result;
                }
                catch (Exception)
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private void SetValue<T>(string key, T value)
        {
            SetValue<T>(key, value, DefaultSection);
        }

        private void SetValue<T>(string key, T value, string section)
        {
            string stringValue = System.Convert.ToString(value, CultureInfo.InvariantCulture);
            HS.SaveINISetting(section, key, stringValue, FileName);
        }

        /// <summary>
        /// Fires event that configuration changed.
        /// </summary>
        public void FireConfigChanged()
        {
            if (ConfigChanged != null)
            {
                var ConfigChangedCopy = ConfigChanged;
                ConfigChangedCopy(this, EventArgs.Empty);
            }
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    configLock.Dispose();
                }
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion IDisposable Support

        private const string NameKey = "Name";
        private const string DeviceIds = "DevicesIds";
        private const string WebServerIPAddressKey = "WebServerIPAddress";
        private const string DebugLoggingKey = "DebugLogging";
        private const string ForwardSpeechKey = "FowardSpeech";
        private const string WebServerPortKey = "WebServerPort";
        private const string SapiVoiceKey = "SAPIVoice";
        private readonly static string FileName = Invariant($"{Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location)}.ini");
        private const string IPAddressKey = "IPAddress";
        private const string VolumeKey = "Volume";
        private const string DefaultSection = "Settings";
        private const char DeviceIdsSeparator = '|';
        private const char PortsEnabledSeparator = ',';
        private readonly Dictionary<string, ChromecastDevice> devices = new Dictionary<string, ChromecastDevice>();
        private readonly IHSApplication HS;
        private ushort webServerPort;
        private bool debugLogging;
        private bool forwardSpeech;
        private string sapiVoice;
        private bool disposedValue = false;
        private readonly ReaderWriterLockSlim configLock = new ReaderWriterLockSlim();
        private IPAddress webServerIPAddress;
    };
}