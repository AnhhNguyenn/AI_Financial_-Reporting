using OpenCvSharp;
using OpenCvSharp.Extensions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BCTC.App.Services.ScanFix
{
    public class DocumentCleaner
    {
        // ==========================================
        // 1. CẤU HÌNH (CONSTANTS)
        // ==========================================

        // Ngưỡng diện tích màu đỏ tối thiểu để kích hoạt xử lý (tránh nhiễu li ti)
        private const double STAMP_COVERAGE_THRESHOLD = 0.0002;

        // Dải màu đỏ 1 (Trong không gian HSV)
        private static readonly Scalar LowerRed1 = new Scalar(0, 30, 30);
        private static readonly Scalar UpperRed1 = new Scalar(15, 255, 255);

        // Dải màu đỏ 2 (HSV vòng qua trục 180 độ)
        private static readonly Scalar LowerRed2 = new Scalar(165, 30, 30);
        private static readonly Scalar UpperRed2 = new Scalar(180, 255, 255);

        // Ngưỡng xác định "Chữ đen" để bảo vệ.
        // Pixel nào tối hơn (nhỏ hơn) giá trị này sẽ được giữ nguyên gốc.
        // Tăng lên (ví dụ 140) nếu chữ vẫn mờ. Giảm xuống (100) nếu bị dính vết dấu đỏ đậm.
        private const double TEXT_PROTECTION_THRESHOLD = 125.0;

        // Bảng tra cứu (LUT) để cân bằng trắng/đen nhanh
        private readonly Mat _contrastLut;

        public DocumentCleaner()
        {
            // Khởi tạo LUT: BlackPoint=20 (nhẹ nhàng để ko mất nét), WhitePoint=230
            _contrastLut = CreateContrastLut(20, 230);
        }

        // ==========================================
        // 2. PUBLIC METHODS (HÀM CHÍNH)
        // ==========================================
        public string CleanAndSavePdf(string inputPdfPath)
        {
            try
            {
                string outputPdfPath = inputPdfPath.Replace(".pdf", "_clean.pdf");

                // Mở PDF bằng PdfSharpCore để đọc cấu trúc (Resource dictionary)
                using var analysisDoc = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Import);

                // Mở PDF bằng PdfiumViewer để Render hình ảnh chất lượng cao
                using var renderDoc = PdfiumViewer.PdfDocument.Load(inputPdfPath);

                // Tạo PDF đầu ra mới
                using var outDoc = new PdfSharpCore.Pdf.PdfDocument();

                for (int i = 0; i < renderDoc.PageCount; i++)
                {
                    // --- BƯỚC 1: DÒ TÌM ĐỘ PHÂN GIẢI GỐC ---
                    // Giúp ảnh đầu ra nét y hệt ảnh gốc, không bị vỡ do sai DPI
                    var (nativeW, nativeH) = GetNativeImageSize(analysisDoc.Pages[i], renderDoc.PageSizes[i]);

                    Log.Information($"[Page {i + 1}] Processing Resolution: {nativeW} x {nativeH}");

                    // --- BƯỚC 2: RENDER ẢNH ---
                    using var img = renderDoc.Render(i, nativeW, nativeH, true);
                    using var bmp = new Bitmap(img);

                    // Chuyển sang OpenCV Mat
                    using var mat = BitmapConverter.ToMat(bmp);

                    // --- BƯỚC 3: XỬ LÝ XÓA DẤU (NẾU CÓ) ---
                    if (CheckForStamp(mat))
                    {
                        RemoveRedStampsSmart(mat);
                    }

                    // --- BƯỚC 4: ĐÓNG GÓI VÀO PDF (Tối ưu dung lượng) ---
                    AddImageToPdfPage(outDoc, mat, renderDoc.PageSizes[i]);
                }

                outDoc.Save(outputPdfPath);
                return outputPdfPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"[DocumentCleaner] Error processing {inputPdfPath}");
                return inputPdfPath; // Trả về file gốc nếu lỗi
            }
        }

        // ==========================================
        // 3. CORE LOGIC: XỬ LÝ ẢNH (OPENCV)
        // ==========================================

        /// <summary>
        /// Logic thông minh: Xóa dấu đỏ nhưng BẢO VỆ chữ đen và chữ ký xanh.
        /// </summary>
        private void RemoveRedStampsSmart(Mat src)
        {
            // Đảm bảo ảnh là 3 kênh màu (BGR)
            if (src.Channels() == 4) Cv2.CvtColor(src, src, ColorConversionCodes.BGRA2BGR);

            // A. TẠO MASK KHU VỰC CÓ MÀU ĐỎ
            using var hsv = new Mat();
            Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);

            using var mask1 = new Mat();
            using var mask2 = new Mat();
            using var redMask = new Mat();

            // Lọc màu đỏ theo 2 dải HSV
            Cv2.InRange(hsv, LowerRed1, UpperRed1, mask1);
            Cv2.InRange(hsv, LowerRed2, UpperRed2, mask2);
            Cv2.BitwiseOr(mask1, mask2, redMask);

            // Nở vùng chọn (Dilate) nhẹ để bao trọn rìa vết mực loang
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.Dilate(redMask, redMask, kernel);

            // B. TÌM CONTOURS (CÁC VÙNG CÓ DẤU)
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(redMask, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // C. XỬ LÝ TỪNG VÙNG
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);

                // Bỏ qua nhiễu quá nhỏ (dưới 20x20 pixel)
                if (rect.Width < 20 || rect.Height < 20) continue;

                // Mở rộng vùng chọn (Padding) an toàn
                int p = 10;
                rect.X = Math.Max(0, rect.X - p);
                rect.Y = Math.Max(0, rect.Y - p);
                rect.Width = Math.Min(src.Width - rect.X, rect.Width + p * 2);
                rect.Height = Math.Min(src.Height - rect.Y, rect.Height + p * 2);

                // Cắt vùng ảnh con (ROI) để xử lý cục bộ
                using var roi = new Mat(src, rect);

                // --- CHIẾN THUẬT: LÀM SẠCH + BẢO VỆ CHỮ ---

                // 1. Tạo bản "Sạch" bằng cách dùng Kênh Đỏ (Red Channel)
                // Kênh đỏ làm vết mực đỏ biến thành sáng (trắng/xám nhạt)
                using var roiRedChannel = roi.ExtractChannel(2); // Channel 2 = Red in BGR

                // Tăng tương phản nhẹ cho kênh đỏ (để vết đỏ mờ đi hẳn, chữ đen đậm lên)
                Cv2.LUT(roiRedChannel, _contrastLut, roiRedChannel);

                // Chuyển bản sạch từ Gray sang BGR để chuẩn bị trộn
                using var roiCleaned = new Mat();
                Cv2.CvtColor(roiRedChannel, roiCleaned, ColorConversionCodes.GRAY2BGR);

                // 2. Tạo Mask "Bảo Vệ Chữ" (Text Protection Mask)
                // Mục tiêu: Tìm xem chỗ nào là Chữ Gốc (đậm màu) để giữ lại.
                using var grayRoi = new Mat();
                Cv2.CvtColor(roi, grayRoi, ColorConversionCodes.BGR2GRAY);

                using var textProtectionMask = new Mat();
                // Threshold: Những pixel nào tối hơn ngưỡng (ví dụ < 125) được coi là TEXT.
                // BinaryInv biến Text thành màu Trắng (255), Nền thành Đen (0).
                Cv2.Threshold(grayRoi, textProtectionMask, TEXT_PROTECTION_THRESHOLD, 255, ThresholdTypes.BinaryInv);

                // 3. Phục hồi chữ gốc vào bản sạch
                // Lệnh này: "Tại những điểm Mask bảo vệ là trắng, hãy chép pixel GỐC (roi) đè lên bản sạch (roiCleaned)"
                // Giúp chữ đen, chữ ký xanh không bị biến đổi màu hay mất nét.
                roi.CopyTo(roiCleaned, textProtectionMask);

                // 4. Dán kết quả cuối cùng vào ảnh lớn
                // Chỉ dán vào những chỗ Mask Đỏ ban đầu (tránh làm ảnh hưởng nền giấy trắng xung quanh)
                using var roiMask = new Mat(redMask, rect);
                roiCleaned.CopyTo(roi, roiMask);
            }
        }

        /// <summary>
        /// Kiểm tra nhanh xem trang giấy có dấu đỏ không để quyết định xử lý.
        /// </summary>
        private bool CheckForStamp(Mat src)
        {
            if (src.Channels() == 4) Cv2.CvtColor(src, src, ColorConversionCodes.BGRA2BGR);

            // Downscale ảnh nhỏ lại để check cho nhanh (tối ưu hiệu năng)
            using var small = new Mat();
            Cv2.Resize(src, small, new OpenCvSharp.Size(src.Width / 4, src.Height / 4));

            using var hsv = new Mat();
            Cv2.CvtColor(small, hsv, ColorConversionCodes.BGR2HSV);

            using var mask = new Mat();
            using var m1 = new Mat();
            using var m2 = new Mat();

            Cv2.InRange(hsv, LowerRed1, UpperRed1, m1);
            Cv2.InRange(hsv, LowerRed2, UpperRed2, m2);
            Cv2.BitwiseOr(m1, m2, mask);

            double coverage = Cv2.CountNonZero(mask) / (double)(small.Rows * small.Cols);
            return coverage > STAMP_COVERAGE_THRESHOLD;
        }

        // ==========================================
        // 4. CÁC HÀM HỖ TRỢ (UTILITIES)
        // ==========================================

        private void AddImageToPdfPage(PdfSharpCore.Pdf.PdfDocument doc, Mat mat, System.Drawing.SizeF originalSize)
        {
            var page = doc.AddPage();
            page.Width = originalSize.Width;
            page.Height = originalSize.Height;

            using var gfx = XGraphics.FromPdfPage(page);
            using var stream = new MemoryStream();
            using var processedBmp = BitmapConverter.ToBitmap(mat);

            // TỐI ƯU DUNG LƯỢNG: Lưu JPEG Quality 90 thay vì PNG
            var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
            var myEncoderParameters = new EncoderParameters(1);
            myEncoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 90L);

            processedBmp.Save(stream, jpgEncoder, myEncoderParameters);
            stream.Position = 0;

            using var xImage = XImage.FromStream(() => stream);
            gfx.DrawImage(xImage, 0, 0, page.Width, page.Height);
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid) return codec;
            }
            return null;
        }

        private Mat CreateContrastLut(double inBlack, double inWhite)
        {
            byte[] lutData = new byte[256];
            for (int k = 0; k < 256; k++)
            {
                double val = (k - inBlack) * 255.0 / (inWhite - inBlack);
                lutData[k] = (byte)Math.Clamp(val, 0, 255);
            }
            using var tempMat = new Mat(1, 256, MatType.CV_8UC1);
            tempMat.SetArray(lutData);
            return tempMat.Clone();
        }

        private (int Width, int Height) GetNativeImageSize(PdfPage page, System.Drawing.SizeF pageSizePoints)
        {
            int maxW = 0;
            int maxH = 0;
            long maxPixels = 0;
            bool foundImage = false;

            try
            {
                var resources = page.Elements.GetDictionary("/Resources");
                if (resources != null)
                {
                    var xObjects = resources.Elements.GetDictionary("/XObject");
                    if (xObjects != null)
                    {
                        foreach (var item in xObjects.Elements)
                        {
                            var reference = item.Value as PdfReference;
                            var xObject = reference?.Value as PdfDictionary;

                            if (xObject != null && xObject.Elements.GetString("/Subtype") == "/Image")
                            {
                                int w = xObject.Elements.GetInteger("/Width");
                                int h = xObject.Elements.GetInteger("/Height");
                                long pixels = (long)w * h;

                                // Lấy ảnh to nhất (bản scan gốc)
                                if (pixels > maxPixels && w > 500 && h > 500)
                                {
                                    maxPixels = pixels;
                                    maxW = w;
                                    maxH = h;
                                    foundImage = true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Cannot extract native resolution: {ex.Message}");
            }

            if (foundImage)
            {
                // Kiểm tra xoay chiều
                bool pageIsLandscape = pageSizePoints.Width > pageSizePoints.Height;
                bool imageIsLandscape = maxW > maxH;

                if (pageIsLandscape != imageIsLandscape)
                    return (maxH, maxW);
                return (maxW, maxH);
            }

            // Fallback: 300 DPI
            int fallbackW = (int)(pageSizePoints.Width * 300 / 72.0);
            int fallbackH = (int)(pageSizePoints.Height * 300 / 72.0);
            return (fallbackW, fallbackH);
        }
    }
}