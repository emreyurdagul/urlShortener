using QRCoder;

namespace LinkService;

public static class QrGenerator
{
    /// <summary>
    /// Renders <paramref name="content"/> as a PNG QR code. Uses QRCoder's
    /// managed PNG encoder (PngByteQRCode) so there is no System.Drawing or
    /// native image dependency to ship in the container.
    /// </summary>
    public static byte[] Png(string content, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
