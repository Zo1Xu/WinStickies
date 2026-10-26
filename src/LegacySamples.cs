using System;
using System.Linq;

namespace WinStickies
{
    // Kept only to identify the original release's auto-generated examples during
    // a one-time migration. These strings are never used to create new notes.
    internal static class LegacySamples
    {
        internal static readonly string[] Content = {
            "给想法一个落脚点\r\n\r\n欢迎使用 WinStickies。\r\n\r\n随手记下一句话、一闪而过的灵感，或今天最重要的事。\r\n\r\n写完就好，我们会自动保存。",
            "今天，慢慢来\r\n\r\n先完成一件重要的小事。\r\n\r\n记得喝水，也记得休息。\r\n\r\n给今天留一点空白。",
            "一闪而过的灵感\r\n\r\n在这里存放还没成形的想法。\r\n\r\n不急着整理，先把它留下来。",
            "收起，也不会消失\r\n\r\n双击顶部标题栏，便签就会收成一条小标题。\r\n\r\n再双击，内容原处展开。\r\n\r\n五张便签，各自记住位置和尺寸。",
            "找到合适的位置\r\n\r\n普通悬浮 · 自由移动\r\n始终置顶 · 一直留在眼前\r\n桌面附着 · 留在桌面层\r\n\r\n点击标题栏右侧的模式图标切换。" };

        internal static bool Matches(Note note)
        {
            return Content.Any(text => string.Equals(text, note.Content, StringComparison.Ordinal));
        }
    }
}
