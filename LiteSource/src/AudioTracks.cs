using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FFmpegForge
{
    /// An audio file added as an extra track of the output video.
    public class ExtraTrack
    {
        public string Path;
        public string Lang = "";       // ISO 639-2 (fre, eng…), "" = not set
        public string Title = "";
        public int DelayMs;            // positive = the track starts later
        public bool Default;
        public override string ToString()
        {
            string n = System.IO.Path.GetFileName(Path);
            var parts = new List<string>();
            if (Lang.Length > 0) parts.Add(Lang);
            if (Title.Length > 0) parts.Add("\"" + Title + "\"");
            if (DelayMs != 0) parts.Add((DelayMs > 0 ? "+" : "") + DelayMs + " ms");
            if (Default) parts.Add("default");
            return n + (parts.Count > 0 ? "   ·   " + string.Join("  ·  ", parts.ToArray()) : "");
        }
    }

    /// 2.1.6: change only the audio of a video (Opus → AAC…) and/or add audio tracks, without re-encoding the picture.
    ///   ffmpeg -i video [-itsoffset d -i track]… -map 0:v -map 0:a -map N:a … -c:v copy -c:a:K <codec | copy> … out
    public static class AudioTracks
    {
        public static readonly string[][] Languages = {
            new[] { "", "Not set" }, new[] { "fre", "French" }, new[] { "eng", "English" }, new[] { "spa", "Spanish" }, new[] { "ger", "German" },
            new[] { "ita", "Italian" }, new[] { "por", "Portuguese" }, new[] { "dut", "Dutch" }, new[] { "jpn", "Japanese" }, new[] { "kor", "Korean" },
            new[] { "chi", "Chinese" }, new[] { "ara", "Arabic" }, new[] { "rus", "Russian" }, new[] { "pol", "Polish" }, new[] { "tur", "Turkish" },
            new[] { "hin", "Hindi" }, new[] { "swe", "Swedish" }, new[] { "nor", "Norwegian" }, new[] { "dan", "Danish" }, new[] { "fin", "Finnish" },
            new[] { "gre", "Greek" }, new[] { "heb", "Hebrew" }, new[] { "ukr", "Ukrainian" }, new[] { "cze", "Czech" }, new[] { "hun", "Hungarian" },
            new[] { "rum", "Romanian" }, new[] { "vie", "Vietnamese" }, new[] { "tha", "Thai" }, new[] { "ind", "Indonesian" }, new[] { "und", "Undetermined" } };

        /// The audio tracks part applies to this export (Video category).
        public static bool Active(ConvSettings s)
        {
            return s.Format != null && s.Format.Kind == MediaKind.Video && (s.KeepVideo || (s.ExtraAudio != null && s.ExtraAudio.Count > 0) || s.ExistingAudio != 0);
        }

        /// Audio codecs a container can hold (FFmpeg codec names). null = no restriction known (MKV, NUT…).
        static string[] Allowed(string ext)
        {
            switch (ext)
            {
                case "mp4": case "m4v": case "3gp": return new[] { "aac", "ac3", "eac3", "mp3", "opus", "flac", "alac", "dts" };
                case "mov": return new[] { "aac", "ac3", "eac3", "mp3", "alac", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_s16be", "pcm_s24be", "flac" };
                case "webm": return new[] { "opus", "vorbis" };
                case "avi": return new[] { "mp3", "ac3", "mp2", "pcm_s16le", "pcm_s24le", "aac" };
                case "ts": case "m2ts": case "mts": return new[] { "aac", "ac3", "eac3", "mp2", "mp3", "dts", "truehd", "opus" };
                case "mpg": case "vob": return new[] { "mp2", "ac3", "pcm_dvd", "dts", "mp3" };
                case "flv": return new[] { "aac", "mp3" };
                case "ogv": return new[] { "vorbis", "opus", "flac" };
                case "wmv": case "asf": return new[] { "wmav2", "mp3" };
                default: return null;
            }
        }

        public static bool Fits(string ext, string codec)
        {
            var a = Allowed(ext);
            return a == null || string.IsNullOrEmpty(codec) || a.Contains(codec);
        }

        /// Audio codec of every audio stream of a file (ffprobe).
        public static List<string> AudioCodecs(string path)
        {
            var l = new List<string>();
            try
            {
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -select_streams a -show_entries stream=codec_name -of csv=p=0 \"" + path + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(20000);
                    foreach (var line in o.Split('\n')) { string c = line.Trim().TrimEnd(','); if (c.Length > 0) l.Add(c); }
                }
            }
            catch { }
            return l;
        }

        /// Turns "-c:a x -b:a 192k -q:a 2 -ar 48000 -ac 2" into per-stream options for output audio stream n.
        static string PerStream(string args, int n)
        {
            string t = " " + (args ?? "").Trim() + " ";
            foreach (var o in new[] { "-c:a", "-b:a", "-q:a", "-ar", "-ac", "-compression_level", "-mapping_family", "-profile:a" })
            {
                string spec = o.Contains(":a") ? o + ":" + n : o + ":a:" + n;
                t = t.Replace(" " + o + " ", " " + spec + " ");
            }
            return t.Trim();
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        public static string Build(Job job, ConvSettings s, string outPath, out double expected, out string error, List<string> notes)
        {
            expected = 0; error = null;
            var fmt = s.Format;
            if (fmt == null || fmt.Kind != MediaKind.Video) { error = "Audio tracks: choose a VIDEO output format."; return null; }
            if (job.Kind != MediaKind.Video && !job.HasVideo) { error = "Audio tracks: the source must be a video."; return null; }
            if (s.SeqExport > 0) { error = "PNG/JPG SEQUENCE has no audio: untick it to change or add audio tracks."; return null; }
            string vkey = fmt.VCodec;
            if (vkey == "gif" || vkey == "apng" || vkey == "webpanim") { error = "GIF / APNG / animated WEBP have no audio: choose another video format."; return null; }
            if (s.KeepVideo)
            {
                var busy = new List<string>();
                if (s.RtxActive) busy.Add("NVIDIA RTX");
                if (s.ScaleActive) busy.Add("Scale / OptiScaler");
                if (s.DnEnabled) busy.Add("Denoiser IA");
                if (s.CaiEnabled) busy.Add("Python / ONNX AI");
                if (s.Vision != null && s.Vision.Enabled) busy.Add("Upscale " + s.Vision.Name);
                if (s.Seed != null && s.Seed.Enabled) busy.Add("SeedVR2");
                if (s.UserVideoChain().Length > 0) busy.Add("video filters");
                if (s.Rotate != 0) busy.Add("rotation");
                if (s.ResW > 0 || s.ResH > 0) busy.Add("resolution");
                if (s.Fps > 0) busy.Add("frame rate");
                if (busy.Count > 0) { error = "\"Keep the video as is\" copies the picture without re-encoding, so it cannot be combined with: " + string.Join(", ", busy.ToArray()) + ". Untick them, or untick \"Keep the video as is\"."; return null; }
            }
            else
            {
                var busy = new List<string>();
                if (s.RtxActive) busy.Add("NVIDIA RTX");
                if (s.ScaleActive) busy.Add("Scale / OptiScaler");
                if (s.Vision != null && s.Vision.Enabled) busy.Add("Upscale " + s.Vision.Name);
                if (s.Seed != null && s.Seed.Enabled) busy.Add("SeedVR2");
                if (busy.Count > 0) { error = "Audio tracks (added tracks / original tracks kept or removed) cannot be combined with " + string.Join(", ", busy.ToArray()) + " in the same export. Convert the video first, then add the audio tracks with \"Keep the video as is\"."; return null; }
            }
            var extra = (s.ExtraAudio ?? new List<ExtraTrack>()).Where(t => t != null && !string.IsNullOrEmpty(t.Path)).ToList();
            foreach (var t in extra) if (!File.Exists(t.Path)) { error = "Audio track not found: " + t.Path; return null; }

            string ext = (fmt.Ext ?? "").ToLowerInvariant();
            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            expected = Math.Max(0, dur - ss);

            // target codec for converted tracks (Audio settings), falling back to the format's own audio codec
            string akey = s.ACodec == "auto" || s.ACodec == "none" || s.ACodec == "copy" || s.ACodec == null ? fmt.ACodec : s.ACodec;
            if (akey == null || akey == "none" || akey.StartsWith("dsf") || akey.StartsWith("dff")) akey = "aac";
            string convArgs = CommandBuilder.AudioArgs(akey, s.AKbps);
            string convCodec = convArgs.Split(' ').SkipWhile(x => x != "-c:a").Skip(1).FirstOrDefault() ?? "aac";
            string userA = s.UserAudioChain();

            var sb = new StringBuilder("-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats ");
            sb.Append(trim).Append("-i ").Append(Q(job.Path)).Append(' ');
            foreach (var t in extra)
            {
                if (t.DelayMs != 0) sb.Append("-itsoffset ").Append(Formats.Inv(t.DelayMs / 1000.0)).Append(' ');
                sb.Append(trim).Append("-i ").Append(Q(t.Path)).Append(' ');
            }

            // ---- video
            sb.Append("-map 0:v:0 ");
            if (s.KeepVideo) sb.Append("-c:v copy ");
            else
            {
                string vk = fmt.VCodec; if (s.VCodec != "auto") vk = s.VCodec;
                if (vk == "copy") sb.Append("-c:v copy ");
                else
                {
                    var vf = new List<string>();
                    CommandBuilder.AddRotate(vf, s.Rotate);
                    string uv = s.UserVideoChain(); if (uv.Length > 0) vf.Add(uv);
                    bool even = CommandBuilder.NeedsEven(vk);
                    string sc = CommandBuilder.ScaleFilter(s, even) ?? (even ? "scale=trunc(iw/2)*2:trunc(ih/2)*2" : null);
                    if (sc != null) vf.Add(sc);
                    if (vf.Count > 0) sb.Append("-filter:v ").Append(Q(string.Join(",", vf.ToArray()))).Append(' ');
                    if (s.Fps > 0) sb.Append("-r ").Append(Formats.Rate(s.Fps)).Append(' ');
                    sb.Append(CommandBuilder.VideoArgs(vk, s, ext)).Append(' ');
                }
            }

            // ---- audio: existing tracks (convert / copy / remove), then the added tracks
            int outIdx = 0;
            var existing = job.HasAudio || !job.Probed ? AudioCodecs(job.Path) : new List<string>();
            int mode = s.ExistingAudio;     // 0 convert, 1 copy, 2 remove
            if (mode != 2)
                for (int i = 0; i < existing.Count; i++)
                {
                    string src = existing[i];
                    bool copy = mode == 1 || (mode == 0 && src == convCodec && userA.Length == 0 && !s.Normalize && s.VolumePct == 100 && s.SampleRate <= 0 && s.AKbps <= 0);
                    if (copy && !Fits(ext, src))
                    {
                        copy = false;
                        if (notes != null) notes.Add("audio track " + (i + 1) + " (" + src + ") does not fit in ." + ext + ": converted to " + convCodec);
                    }
                    sb.Append("-map 0:a:").Append(i).Append(' ');
                    AppendCodec(sb, outIdx, copy, convArgs, s, userA, akey);
                    outIdx++;
                }
            bool anyDefault = extra.Any(t => t.Default);
            for (int k = 0; k < extra.Count; k++)
            {
                var t = extra[k];
                var codecs = AudioCodecs(t.Path);
                if (codecs.Count == 0) { error = "No audio stream in " + Path.GetFileName(t.Path) + "."; return null; }
                string src = codecs[0];
                // the added track is kept bit-for-bit when the container accepts it, otherwise converted
                bool copy = Fits(ext, src) && src != "dsd_lsbf" && src != "dsd_msbf" && src != "dsd_lsbf_planar" && src != "dsd_msbf_planar";
                if (!copy && notes != null) notes.Add(Path.GetFileName(t.Path) + " (" + src + ") does not fit in ." + ext + ": converted to " + convCodec);
                sb.Append("-map ").Append(k + 1).Append(":a:0 ");
                AppendCodec(sb, outIdx, copy, convArgs, s, "", akey);
                if (t.Lang.Length > 0) sb.Append("-metadata:s:a:").Append(outIdx).Append(" language=").Append(t.Lang).Append(' ');
                if (t.Title.Length > 0)
                {
                    sb.Append("-metadata:s:a:").Append(outIdx).Append(" title=").Append(Q(t.Title)).Append(' ');
                    if (ext == "mp4" || ext == "m4v" || ext == "mov" || ext == "3gp")   // MP4 / MOV show the track name from the handler name
                        sb.Append("-metadata:s:a:").Append(outIdx).Append(" handler_name=").Append(Q(t.Title)).Append(' ');
                }
                outIdx++;
            }
            if (outIdx == 0) sb.Append("-an ");
            // default track: the one ticked, otherwise the first one
            if (outIdx > 0)
            {
                int existingCount = mode == 2 ? 0 : existing.Count;
                for (int i = 0; i < outIdx; i++)
                {
                    bool def = anyDefault ? (i >= existingCount && extra[i - existingCount].Default) : i == 0;
                    sb.Append("-disposition:a:").Append(i).Append(def ? " default " : " 0 ");
                }
            }

            sb.Append("-disposition:v:0 default ");
            // ---- subtitles (MKV keeps them), chapters and metadata
            if (ext == "mkv" || ext == "mka" || ext == "nut") sb.Append("-map 0:s? -c:s copy ");
            else sb.Append("-sn ");
            sb.Append("-map_metadata 0 -map_chapters 0 ");
            if (!string.IsNullOrEmpty(fmt.Extra)) sb.Append(fmt.Extra).Append(' ');
            if (fmt.Dynamic && fmt.Muxer != null) sb.Append("-f ").Append(fmt.Muxer).Append(' ');
            if (!string.IsNullOrWhiteSpace(s.CustomArgs)) sb.Append(s.CustomArgs.Trim()).Append(' ');
            sb.Append("-strict -2 ").Append(Q(outPath));
            return sb.ToString();
        }

        static void AppendCodec(StringBuilder sb, int n, bool copy, string convArgs, ConvSettings s, string userA, string akey)
        {
            if (copy) { sb.Append("-c:a:").Append(n).Append(" copy "); return; }
            var af = new List<string>();
            string lay = ChannelLayouts.Filter(s.ChLayout);
            if (lay != null) af.Add(lay);
            if (s.VolumePct != 100) af.Add("volume=" + Formats.Inv(s.VolumePct / 100.0));
            if (userA.Length > 0) af.Add(userA);
            if (s.Normalize) af.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
            if (af.Count > 0) sb.Append("-filter:a:").Append(n).Append(' ').Append(Q(string.Join(",", af.ToArray()))).Append(' ');
            sb.Append(PerStream(convArgs, n)).Append(' ');
            if (s.SampleRate > 0) sb.Append("-ar:a:").Append(n).Append(' ').Append(s.SampleRate).Append(' ');
            else if (s.Normalize) sb.Append("-ar:a:").Append(n).Append(" 48000 ");
            string ex = ChannelLayouts.ExtraArgs(s.ChLayout, akey);
            if (!string.IsNullOrEmpty(ex)) sb.Append(PerStream(ex, n)).Append(' ');
        }
    }
}
