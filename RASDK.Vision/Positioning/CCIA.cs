﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace RASDK.Vision.Positioning
{
    /// <summary>
    /// Vision positioning by Camera Calibration with Iterative Approximation.<br/>
    /// 疊代逼近相機標定視覺定位法。
    /// </summary>
    public class CCIA : IVisionPositioning
    {
        /// <summary>
        /// 反轉X軸。
        /// </summary>
        public bool InvertedX = false;

        /// <summary>
        /// 反轉Y軸。
        /// </summary>
        /// <remarks>
        /// 圖片和手臂座標系的Y軸通常是相反的，因此預設爲true。
        /// </remarks>
        public bool InvertedY = true;

        public double InterativeTimeout = 1.5;

        public double BreakPixelError = 0.25;

        private readonly CameraParameter _cameraParameter;

        private readonly Approximation _approximation;

        private readonly TransferFunctionOfVirtualCheckBoardToWorld _transferFunctionOfVirtualCheckBoardToWorld;

        private readonly Timer _interativeTimer;

        private double _allowablePixelError;

        private double _interativeTimerCount = 0;

        /// <summary>
        /// Vision positioning by Camera Calibration with Iterative Approximation.<br/>
        /// 疊代逼近相機標定視覺定位法。
        /// </summary>
        /// <remarks>
        /// 此方法的運作方式爲先給定一個預測虛擬定位板座標作，將其透過相機標定法來算出對應的預測像素座標。<br/>
        /// 如果預測像素座標與實際像素座標的差距大於容許誤差，就調整預測虛擬定位板座標，再重複上述步驟。<br/>
        /// 如果預測像素座標與實際像素座標的差距小於等於容許誤差，就視目前的預測虛擬定位板座標爲正確的，再將其透過一組變換來轉換成世界座標。<br/>
        /// 虛擬定位板座標是一個以相機成像平面投影到實物平面的假想平面座標系。其原點在鏡頭中心，也就是主點的投影位置。
        /// </remarks>
        public CCIA(CameraParameter cameraParameter,
                    double allowablePixelError = 5,
                    TransferFunctionOfVirtualCheckBoardToWorld tf = null,
                    Approximation approximation = null)
        {
            _cameraParameter = cameraParameter;
            _allowablePixelError = allowablePixelError;
            _transferFunctionOfVirtualCheckBoardToWorld = tf ?? BasicTransferFunctionOfVirtualCheckBoardToWorld;
            _approximation = approximation ?? BasicApproximation;

            _interativeTimer = new Timer(100);
            _interativeTimer.Elapsed += (s, e) => { _interativeTimerCount += 0.1; };
            _interativeTimer.Stop();

            WorldOffset = new PointF(0, 0);
        }

        /// <summary>
        /// 誤差逼近算法。
        /// </summary>
        public delegate void Approximation(double errorX,
                                           double errorY,
                                           ref double valueX,
                                           ref double valueY);

        /// <summary>
        /// 座標轉換算法。
        /// </summary>
        public delegate void TransferFunctionOfVirtualCheckBoardToWorld(double vX,
                                                                        double vY,
                                                                        out double armX,
                                                                        out double armY);

        /// <summary>
        /// 世界座標偏移。
        /// </summary>
        public PointF WorldOffset { get; set; }

        public double AllowableError
        {
            get => _allowablePixelError;
            set => _allowablePixelError = value;
        }

        public PointF ImageToWorld(PointF pixel)
        {
            ImageToWorld((double)pixel.X, (double)pixel.Y, out var worldX, out var worldY);
            return new PointF((float)worldX, (float)worldY);
        }

        public void ImageToWorld(double pixelX, double pixelY, out double worldX, out double worldY)
        {
            // 給定一個預測虛擬定位板座標。
            double virtualCheckBoardX = 0;
            double virtualCheckBoardY = 0;

            var acceptable = false; // 結果可接受。
            var accuracy = false; // 結果可以更精確。
            var allowableError = _allowablePixelError;
            var error = new PointF();

            _interativeTimerCount = 0;
            _interativeTimer.Start();
            while (_interativeTimerCount < InterativeTimeout)
            {
                acceptable = ImageToWorldInterative(_cameraParameter,
                                                    pixelX,
                                                    pixelY,
                                                    virtualCheckBoardX,
                                                    virtualCheckBoardY,
                                                    allowableError,
                                                    _approximation,
                                                    out var resultX,
                                                    out var resultY,
                                                    out error);

                virtualCheckBoardX = resultX;
                virtualCheckBoardY = resultY;

                if (acceptable)
                {
                    // 時間未到但結果已可接受，進一步降低容許誤差以更精確地求值。
                    accuracy = true;
                    allowableError = Math.Max(Math.Abs(error.X), Math.Abs(error.Y)) - 0.1;

                    if (allowableError <= BreakPixelError)
                    {
                        // 誤差已經足夠小，強行離開。
                        break;
                    }
                }
            }
            _interativeTimer.Stop();

            if (!acceptable && !accuracy)
            {
                throw new TimeoutException($"CCIA image to world timeout, final error X:{error.X}, Y:{error.Y} .");
            }

            // 將虛擬定位板座標轉換成世界座標。
            _transferFunctionOfVirtualCheckBoardToWorld(virtualCheckBoardX,
                                                        virtualCheckBoardY,
                                                        out worldX,
                                                        out worldY);

            // 座標偏移。
            if (WorldOffset == null)
            {
                WorldOffset = new PointF(0, 0);
            }
            worldX += WorldOffset.X;
            worldY += WorldOffset.Y;

            if (InvertedX)
            {
                worldX = -worldX;
            }

            if (InvertedY)
            {
                worldY = -worldY;
            }
        }

        private bool ImageToWorldInterative(CameraParameter cameraParameter,
                                            double pixelX,
                                            double pixelY,
                                            double initWorldX,
                                            double initWorldY,
                                            double allowableError,
                                            Approximation approximation,
                                            out double resultWorldX,
                                            out double resultworldY,
                                            out PointF error)
        {
            if (allowableError <= 0)
            {
                throw new ArgumentException($"‘allowableError’必須大於0，實際值爲{allowableError}。");
            }

            var acceptable = false;
            var objPoint = new MCvPoint3D32f((float)initWorldX, (float)initWorldY, 0);

            // 將預測虛擬定位板座標透過相機標定法來算出對應的預測像素座標。
            var forecastPixel = CvInvoke.ProjectPoints(new[] { objPoint },
                                                       cameraParameter.RotationVector,
                                                       cameraParameter.TranslationVector,
                                                       cameraParameter.IntrinsicMatrix,
                                                       cameraParameter.DistortionCoefficients);

            // 計算預測像素座標與實際像素座標的差距。
            error = new PointF(0, 0)
            {
                X = (float)pixelX - forecastPixel[0].X,
                Y = (float)pixelY - forecastPixel[0].Y
            };

            // 判定差距是否大於容許誤差。
            if (Math.Abs(error.X) > allowableError || Math.Abs(error.Y) > allowableError)
            {
                // 差距大於容許誤差，調整預測虛擬定位板座標。
                approximation(error.X, error.Y, ref initWorldX, ref initWorldY);
                acceptable = false;
            }
            else
            {
                acceptable = true;
            }

            resultWorldX = initWorldX;
            resultworldY = initWorldY;
            return acceptable;
        }

        private void BasicTransferFunctionOfVirtualCheckBoardToWorld(double vX,
                                                                     double vY,
                                                                     out double worldX,
                                                                     out double worldY)
        {
            worldX = vX;
            worldY = vY;
        }

        private void BasicApproximation(double errorX, double errorY, ref double vX, ref double vY)
        {
            var kp = 0.03;
            vX += errorX * kp;
            vY += errorY * kp;
        }
    }
}