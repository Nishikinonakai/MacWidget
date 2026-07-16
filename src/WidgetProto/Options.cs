namespace WidgetProto;

public sealed class Options
{
    public int N = 1;
    public string Control = "hwnd";     // hwnd | comp | native
    public string Backdrop = "acrylic"; // none | mica | acrylic | tabbed
    public string Origin = "same";      // same(全部 widgets.test) | multi(每组件独立 wN.test，强制分 renderer)
    public string Pin = "bottom";       // bottom | none
    public string Widget = "mixed";     // mixed | clock | monitor | weather | photo
    public string Glass = "extend";     // extend(DwmExtendFrameIntoClientArea -1) | none
    public bool Dark = true;
    public bool NoActivate = true;
    public bool ProcPerSite = false;    // Chromium --process-per-site：强制同 site 合并 renderer（实验）

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--n": _ = int.TryParse(Next(), out o.N); break;
                case "--control": o.Control = Next(); break;
                case "--backdrop": o.Backdrop = Next(); break;
                case "--origin": o.Origin = Next(); break;
                case "--pin": o.Pin = Next(); break;
                case "--widget": o.Widget = Next(); break;
                case "--glass": o.Glass = Next(); break;
                case "--light": o.Dark = false; break;
                case "--activate": o.NoActivate = false; break;
                case "--procpersite": o.ProcPerSite = true; break;
            }
        }
        if (o.N < 1) o.N = 1;
        return o;
    }
}
