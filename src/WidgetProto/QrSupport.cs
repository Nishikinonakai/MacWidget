using System.Text;
using System.Text.Json;
using QRCoder;

namespace WidgetProto;

internal static class QrSupport
{
    internal const int MaxUtf8Bytes = 1000;

    public static void Render(WidgetWindow owner, string? value)
    {
        string text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            owner.PostJson(JsonSerializer.Serialize(new { t = "qrResult", data = (string?)null, error = (string?)null }));
            return;
        }
        int bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > MaxUtf8Bytes)
        {
            owner.PostJson(JsonSerializer.Serialize(new { t = "qrResult", data = (string?)null,
                error = Ui.T($"内容过长（{bytes}/{MaxUtf8Bytes} 字节）", $"Text is too long ({bytes}/{MaxUtf8Bytes} bytes)") }));
            return;
        }
        try
        {
            using var qrData = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qr = new PngByteQRCode(qrData);
            var png = qr.GetGraphic(8);
            owner.PostJson(JsonSerializer.Serialize(new
            {
                t = "qrResult",
                data = "data:image/png;base64," + Convert.ToBase64String(png),
                error = (string?)null,
                bytes
            }));
        }
        catch (Exception ex)
        {
            Program.Log("QR generation failed: " + ex.Message);
            owner.PostJson(JsonSerializer.Serialize(new { t = "qrResult", data = (string?)null,
                error = Ui.T("无法生成二维码。", "Could not generate the QR code.") }));
        }
    }
}
