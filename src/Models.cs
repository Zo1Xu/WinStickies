using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace WinStickies
{
    [DataContract]
    public enum NoteMode
    {
        [EnumMember] Floating,
        [EnumMember] Topmost,
        [EnumMember] Desktop
    }

    [DataContract]
    public sealed class Note
    {
        public const float DefaultFontSize = 11.25f;
        public const int DefaultPriority = 2;
        [DataMember] public int Priority = DefaultPriority;
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Content = "";
        [DataMember(EmitDefaultValue = false)] public string ContentRtf;
        [DataMember] public string CustomTitle = "";
        [DataMember] public float FontSize = DefaultFontSize;
        [DataMember] public int ColorIndex;
        [DataMember] public NoteMode Mode;
        [DataMember] public bool Collapsed;
        [DataMember] public bool Archived;
        [DataMember] public int X;
        [DataMember] public int Y;
        // Expanded dimensions are NEVER overwritten by the collapsed window's bounds.
        [DataMember] public int ExpandedWidth = 310;
        [DataMember] public int ExpandedHeight = 300;
        // Zero keeps old notebooks compatible until the title strip is first used.
        [DataMember] public int CollapsedWidth;
        [DataMember] public int CollapsedHeight;
        [DataMember] public DateTime ModifiedUtc = DateTime.UtcNow;

        public string Title
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomTitle)) return CustomTitle;
                string content = Content ?? "";
                int start = 0;
                while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
                if (start == content.Length) return Strings.UntitledNote;
                int end = start;
                while (end < content.Length && content[end] != '\r' && content[end] != '\n') end++;
                while (end > start && char.IsWhiteSpace(content[end - 1])) end--;
                // The rest of the document never needs to be split or copied for a title.
                return content.Substring(start, end - start);
            }
        }

        internal string Preview(int maxLength)
        {
            if (maxLength <= 0) return "";
            string content = Content ?? "";
            int start = 0;
            while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
            if (string.IsNullOrWhiteSpace(CustomTitle))
            {
                // Match the overview's first-line omission, including CRLF input.
                while (start < content.Length && content[start] != '\n') start++;
                while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
            }
            var preview = new StringBuilder(Math.Min(maxLength, content.Length - start));
            for (int i = start; i < content.Length && preview.Length < maxLength; i++)
                if (content[i] != '\r') preview.Append(content[i]);
            // Do not cut an emoji between its UTF-16 surrogate pair.
            if (preview.Length > 0 && char.IsHighSurrogate(preview[preview.Length - 1])) preview.Length--;
            return preview.ToString().TrimEnd();
        }

        public Rectangle VisibleBounds(int titleHeight)
        {
            return new Rectangle(X, Y, Collapsed && CollapsedWidth > 0 ? CollapsedWidth : ExpandedWidth,
                Collapsed ? (CollapsedHeight > 0 ? CollapsedHeight : titleHeight) : ExpandedHeight);
        }

        public void CaptureBounds(Rectangle bounds)
        {
            X = bounds.X;
            Y = bounds.Y;
            if (Collapsed)
            {
                CollapsedWidth = bounds.Width;
                CollapsedHeight = bounds.Height;
            }
            else
            {
                ExpandedWidth = bounds.Width;
                ExpandedHeight = bounds.Height;
            }
        }

        public void Normalize()
        {
            Priority = NormalizePriority(Priority);
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Content == null) Content = "";
            CustomTitle = CleanTitle(CustomTitle);
            FontSize = NormalizeFontSize(FontSize);
            ColorIndex = Math.Max(0, Math.Min(5, ColorIndex));
            if (!Enum.IsDefined(typeof(NoteMode), Mode)) Mode = NoteMode.Floating;
            ExpandedWidth = Math.Max(200, Math.Min(6000, ExpandedWidth));
            ExpandedHeight = Math.Max(140, Math.Min(6000, ExpandedHeight));
            CollapsedWidth = CollapsedWidth <= 0 ? 0 : Math.Max(180, Math.Min(6000, CollapsedWidth));
            CollapsedHeight = CollapsedHeight <= 0 ? 0 : Math.Max(30, Math.Min(6000, CollapsedHeight));
        }

        internal static string CleanTitle(string value)
        { return (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim(); }

        internal static float NormalizeFontSize(float value)
        { return float.IsNaN(value) || float.IsInfinity(value) || value <= 0 ? DefaultFontSize : Math.Max(9, Math.Min(36, value)); }

        internal static int NormalizePriority(int value)
        { return value >= 0 && value <= 3 ? value : DefaultPriority; }

        // DataContract deserialization bypasses field initializers. Old notes are P2, not P0.
        [OnDeserializing]
        private void InitializePriority(StreamingContext context) { Priority = DefaultPriority; }
    }

    [DataContract]
    public sealed class Notebook
    {
        [DataMember] public int Version = 1;
        // Missing in the original release; deserialization leaves it at zero.
        [DataMember] public int StartupRevision = 1;
        [DataMember] public List<Note> Notes = new List<Note>();
        [DataMember] public bool SortByPriority;

        internal IEnumerable<Note> OrderedNotes
        {
            // OrderBy is stable: ties retain notebook order without rewriting the list.
            get { return SortByPriority ? Notes.OrderBy(n => n.Priority) : (IEnumerable<Note>)Notes; }
        }
    }

    public static class Placement
    {
        // Keep the title bar reachable after monitor removal, including negative monitor origins.
        public static Rectangle KeepReachable(Rectangle bounds, IEnumerable<Rectangle> workingAreas, int titleHeight)
        {
            var areas = workingAreas.ToArray();
            if (areas.Length == 0) return bounds;
            titleHeight = Math.Min(titleHeight, bounds.Height);
            var title = new Rectangle(bounds.X, bounds.Y, bounds.Width, titleHeight);
            if (areas.Any(a => Rectangle.Intersect(a, title).Width >= Math.Min(120, bounds.Width) &&
                               Rectangle.Intersect(a, title).Height >= titleHeight)) return bounds;
            var nearest = areas.OrderBy(a => Math.Pow((double)a.X + a.Width / 2 - bounds.X, 2) +
                                            Math.Pow((double)a.Y + a.Height / 2 - bounds.Y, 2)).First();
            bounds.X = Math.Max(nearest.Left, Math.Min(bounds.X, nearest.Right - Math.Min(bounds.Width, nearest.Width)));
            bounds.Y = Math.Max(nearest.Top, Math.Min(bounds.Y, nearest.Bottom - titleHeight));
            return bounds;
        }
    }
}
