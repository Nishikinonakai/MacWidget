namespace WidgetProto;

public sealed class Options
{
    public int N = 4;
    public string Control = "hwnd";     // hwnd | comp | native（实验对照保留）
    public string Backdrop = "none";    // none | mica | acrylic | tabbed | wca —— 卡面材质已全 CSS 化，默认不动 DWM
    public string Origin = "multi";     // same | multi（配合 --process-per-site：独立 site 但 renderer 合并）
    public string Pin = "bottom";       // bottom | none
    public string Widget = "mixed";     // 实验模式的组件种类
    public string Glass = "extend";     // extend（透明表面防黑底，必须）| none
    public string Style = "auto";       // auto | full | mono —— Widget style 三档（macOS 语义）
    public bool Dark = true;            // 实验模式初值；产品模式由 ColorMode 每 tick 读注册表接管
    public bool NoActivate = true;
    public bool ProcPerSite = true;     // 内存实验定案：--process-per-site 是胜负手，默认开
    public bool LabMode;                // --n/--widget 显式给出 = 实验模式（栅格铺开、不读写持久化布局）

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (args[i])
            {
                case "--n": _ = int.TryParse(Next(), out o.N); o.LabMode = true; break;
                case "--control": o.Control = Next(); break;
                case "--backdrop": o.Backdrop = Next(); break;
                case "--origin": o.Origin = Next(); break;
                case "--pin": o.Pin = Next(); break;
                case "--widget": o.Widget = Next(); o.LabMode = true; break;
                case "--glass": o.Glass = Next(); break;
                case "--style": o.Style = Next(); break;
                case "--light": o.Dark = false; break;
                case "--activate": o.NoActivate = false; break;
                case "--procpersite": o.ProcPerSite = true; break;
                case "--no-procpersite": o.ProcPerSite = false; break;
            }
        }
        if (o.N < 1) o.N = 1;
        if (o.Style is not ("auto" or "full" or "mono")) o.Style = "auto";
        return o;
    }
}
