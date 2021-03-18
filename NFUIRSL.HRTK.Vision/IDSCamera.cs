﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using uEye;

namespace NFUIRSL.HRTK.Vision
{
    public enum CaptureMode
    {
        FreeRun,
        Stop,
        Snapshot
    }

    public class IDSCamera
    {
        private uEye.Camera Camera;
        private IMessage Message;
        private const int CnNumberOfSeqBuffers = 3;
        private PictureBox PictureBox;
        private Timer UpdateTimer;

        public int DeviceId { get; private set; }
        public int CameraId { get; private set; }
        public Boolean IsLive { get; private set; }
        public uEye.Defines.DisplayRenderMode RenderMode { get; private set; }
        public Int32 FrameCount { get; private set; }
        public double Fps { get; private set; }
        public string Failed { get; private set; }
        public string SensorName { get; private set; }

        #region - Auto Features -

        private delegate uEye.Defines.Status GetFunc(out bool enable);

        private bool GetAutoFeatures(GetFunc func)
        {
            func(out bool enable);
            return enable;
        }


        public bool AutoShutter
        {
            get => GetAutoFeatures(Camera.AutoFeatures.Software.Shutter.GetEnable);
            set => Camera.AutoFeatures.Software.Shutter.SetEnable(value);
        }

        public bool AutoWhiteBalance
        {
            get => GetAutoFeatures(Camera.AutoFeatures.Software.WhiteBalance.GetEnable);
            set => Camera.AutoFeatures.Software.WhiteBalance.SetEnable(value);
        }

        public bool AutoGain
        {
            get => GetAutoFeatures(Camera.AutoFeatures.Software.Gain.GetEnable);
            set => Camera.AutoFeatures.Software.Gain.SetEnable(value);
        }

        #endregion

        public IDSCamera(IMessage message)
        {
            if (CheckRuntimeVersion())
            {
                Message = message;

                DeviceId = 1;
                CameraId = 1;
                IsLive = false;
                RenderMode = uEye.Defines.DisplayRenderMode.FitToWindow;

                UpdateTimer = new Timer();
                UpdateTimer.Interval = 100;
                UpdateTimer.Tick += UpdateControls;
            }
            else
            {
                Message.Show(".NET Runtime Version 3.5.0 is required", LoggingLevel.Error);
            }
        }

        public IDSCamera(PictureBox pictureBox, IMessage message)
        {
            if (CheckRuntimeVersion())
            {
                Message = message;

                PictureBox = pictureBox;
                PictureBox.SizeMode = PictureBoxSizeMode.CenterImage;

                DeviceId = 1;
                CameraId = 1;
                IsLive = false;
                RenderMode = uEye.Defines.DisplayRenderMode.FitToWindow;

                UpdateTimer = new Timer();
                UpdateTimer.Interval = 100;
                UpdateTimer.Tick += UpdateControls;
            }
            else
            {
                Message.Show(".NET Runtime Version 3.5.0 is required", LoggingLevel.Error);
            }
        }

        ~IDSCamera()
        {
            Exit();
        }

        public void Open(CaptureMode captureMode = CaptureMode.FreeRun, bool autoFeatures = true)
        {
            if (Camera != null)
            {
                var status = ChangeCaptureMode(captureMode);
                if (status != uEye.Defines.Status.SUCCESS && Camera.IsOpened)
                {
                    Camera.Exit();
                }
                else
                {
                    AutoGain = autoFeatures;
                    AutoShutter = autoFeatures;
                    AutoWhiteBalance = autoFeatures;
                }
            }
            else
            {
                Message.Show("Camera never initialization.", LoggingLevel.Warn);
            }
        }

        public uEye.Defines.Status ChangeCaptureMode(CaptureMode captureMode)
        {
            Func<uEye.Defines.Status> func;
            bool expectIsLive;

            switch (captureMode)
            {
                case CaptureMode.FreeRun:
                    func = Camera.Acquisition.Capture;
                    expectIsLive = true;
                    break;

                case CaptureMode.Stop:
                    func = Camera.Acquisition.Stop;
                    expectIsLive = false;
                    break;

                case CaptureMode.Snapshot:
                    func = Camera.Acquisition.Freeze;
                    expectIsLive = false;
                    break;

                default:
                    return uEye.Defines.Status.NO_SUCCESS;
            }

            var status = func();
            if (status != uEye.Defines.Status.SUCCESS)
            {
                Message.Show("Starting live video failed", LoggingLevel.Error);
            }
            else
            {
                // Everything is ok.
                IsLive = expectIsLive;
            }
            return status;
        }

        public void Exit()
        {
            UpdateTimer.Stop();
            IsLive = false;

            Camera.EventFrame -= FrameEvent;
            Camera.Exit();

            PictureBox.Invalidate();
            PictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
            RenderMode = uEye.Defines.DisplayRenderMode.FitToWindow;
        }

        public void ChooseCamera()
        {
            var chooseForm = new CameraChoose();
            if (chooseForm.ShowDialog() == DialogResult.OK)
            {
                DeviceId = chooseForm.DeviceID;
                CameraId = chooseForm.CameraID;
            }
        }

        public void ShowSettingForm()
        {
            if (Camera != null)
            {
                var settingForm = new SettingsForm(Camera);
                settingForm.SizeControl.AOIChanged += OnDisplayChanged;
                settingForm.FormatControl.DisplayChanged += OnDisplayChanged;
                settingForm.ShowDialog();
            }
            else
            {
                Message.Show("Camera never initialization.", LoggingLevel.Warn);
            }
        }

        public Bitmap GetImage()
        {
            Bitmap img = null;
            if (Camera != null)
            {
                Camera.Memory.GetLast(out int memoryId);
                Camera.Memory.ToBitmap(memoryId, out img);
            }
            return img;
        }

        #region - .NET Version -

        private bool CheckRuntimeVersion()
        {
            var versionMin = new Version(3, 5);
            var ok = false;

            foreach (Version version in InstalledDotNetVersions())
            {
                if (version >= versionMin)
                {
                    ok = true;
                    break;
                }
            }

            return ok;
        }

        private System.Collections.ObjectModel.Collection<Version> InstalledDotNetVersions()
        {
            var versions = new System.Collections.ObjectModel.Collection<Version>();
            var NDPKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP");
            if (NDPKey != null)
            {
                string[] subkeys = NDPKey.GetSubKeyNames();
                foreach (string subkey in subkeys)
                {
                    GetDotNetVersion(NDPKey.OpenSubKey(subkey), subkey, versions);
                    GetDotNetVersion(NDPKey.OpenSubKey(subkey).OpenSubKey("Client"), subkey, versions);
                    GetDotNetVersion(NDPKey.OpenSubKey(subkey).OpenSubKey("Full"), subkey, versions);
                }
            }
            return versions;
        }

        private void GetDotNetVersion(Microsoft.Win32.RegistryKey parentKey,
                                      string subVersionName,
                                      System.Collections.ObjectModel.Collection<Version> versions)
        {
            if (parentKey != null)
            {
                string installed = Convert.ToString(parentKey.GetValue("Install"));
                if (installed == "1")
                {
                    string version = Convert.ToString(parentKey.GetValue("Version"));
                    if (string.IsNullOrEmpty(version))
                    {
                        if (subVersionName.StartsWith("v"))
                            version = subVersionName.Substring(1);
                        else
                            version = subVersionName;
                    }

                    Version ver = new Version(version);

                    if (!versions.Contains(ver))
                        versions.Add(ver);
                }
            }
        }

        #endregion

        public uEye.Defines.Status Init()
        {
            if (Camera == null)
            {
                Camera = new uEye.Camera();
            }

            var id = DeviceId | (Int32)uEye.Defines.DeviceEnumeration.UseDeviceID;

            var status = PictureBox == null ? Camera.Init(id) : Camera.Init(id, PictureBox.Handle);
            if (status != uEye.Defines.Status.SUCCESS)
            {
                Message.Show("Initializing the camera failed", LoggingLevel.Error);
                return status;
            }

            status = MemoryHelper.AllocImageMems(Camera, CnNumberOfSeqBuffers);
            if (status != uEye.Defines.Status.SUCCESS)
            {
                Message.Show("Allocating memory failed", LoggingLevel.Error);
                return status;
            }

            status = MemoryHelper.InitSequence(Camera);
            if (status != uEye.Defines.Status.SUCCESS)
            {
                Message.Show("Add to sequence failed", LoggingLevel.Error);
                return status;
            }

            Camera.EventFrame += FrameEvent;
            FrameCount = 0;
            UpdateTimer.Start();
            uEye.Types.SensorInfo sensorInfo;
            Camera.Information.GetSensorInfo(out sensorInfo);
            SensorName = sensorInfo.SensorName;
            PictureBox.SizeMode = PictureBoxSizeMode.Normal;
            return status;
        }

        private void FrameEvent(object sender, EventArgs e)
        {
            var camera = sender as uEye.Camera;
            if (camera.IsOpened)
            {
                uEye.Defines.DisplayMode mode;
                camera.Display.Mode.Get(out mode);

                // Only display in DiB mode.
                if (mode == uEye.Defines.DisplayMode.DiB)
                {
                    Int32 memId;
                    var status = camera.Memory.GetLast(out memId);
                    if ((status == uEye.Defines.Status.SUCCESS) && 0 < memId)
                    {
                        if (camera.Memory.Lock(memId) == uEye.Defines.Status.SUCCESS)
                        {
                            camera.Display.Render(memId, RenderMode);
                            camera.Memory.Unlock(memId);
                        }
                    }
                }
                ++FrameCount;
            }
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            uEye.Defines.DisplayMode displayMode;
            Camera.Display.Mode.Get(out displayMode);

            // set scaling options
            if (displayMode != uEye.Defines.DisplayMode.DiB)
            {
                if (RenderMode == uEye.Defines.DisplayRenderMode.DownScale_1_2)
                {
                    RenderMode = uEye.Defines.DisplayRenderMode.Normal;

                    PictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                    // get image size
                    System.Drawing.Rectangle rect;
                    Camera.Size.AOI.Get(out rect);

                    PictureBox.Width = rect.Width;
                    PictureBox.Height = rect.Height;
                }
                else
                {
                    Camera.DirectRenderer.SetScaling(RenderMode == uEye.Defines.DisplayRenderMode.FitToWindow);
                }
            }
            else
            {
                if (RenderMode != uEye.Defines.DisplayRenderMode.FitToWindow)
                {
                    PictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                    // get image size
                    System.Drawing.Rectangle rect;
                    Camera.Size.AOI.Get(out rect);

                    if (RenderMode != uEye.Defines.DisplayRenderMode.Normal)
                    {

                        PictureBox.Width = rect.Width / 2;
                        PictureBox.Height = rect.Height / 2;
                    }
                    else
                    {
                        PictureBox.Width = rect.Width;
                        PictureBox.Height = rect.Height;
                    }
                }
            }
        }

        private void UpdateControls(object sender, EventArgs e)
        {
            Camera.Timing.Framerate.GetCurrentFps(out var frameRate);
            Fps = frameRate;

            Camera.Information.GetCaptureStatus(out var captureStatus);
            if (null != captureStatus)
            {
                Failed = "" + captureStatus.Total;
            }
        }
    }
}