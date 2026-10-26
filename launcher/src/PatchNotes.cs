using System;
using System.Drawing;
using System.Text;

namespace CopperLauncher
{
    // Turns patchnotes.txt into formatted text for the patch notes box.
    //
    // Format (same as tools/patchnotes.txt):
    //   # Version 1.9.3        version heading
    //   ## Bug Fixes           section heading
    //   - Fixed a thing.       bullet
    //     - nested detail      bullet, indented two spaces per level
    //   Anything else          plain paragraph
    static class PatchNotes
    {
        public static string ToRtf(string text, Color body, Color heading, Color section, Color muted)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil Segoe UI;}}");
            sb.Append(@"{\colortbl ;").Append(C(body)).Append(C(heading)).Append(C(section)).Append(C(muted)).Append("}");
            sb.Append(@"\viewkind4\uc1");

            bool first = true;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;

                if (line.StartsWith("## "))
                {
                    sb.Append(@"\pard\sb160\sa40\cf3\b\fs21 ").Append(Esc(line.Substring(3))).Append(@"\b0\par");
                }
                else if (line.StartsWith("# "))
                {
                    sb.Append(first ? @"\pard\sb0" : @"\pard\sb360").Append(@"\sa20\cf2\b\fs30 ")
                      .Append(Esc(line.Substring(2))).Append(@"\b0\par");
                }
                else
                {
                    int spaces = 0;
                    while (spaces < line.Length && line[spaces] == ' ') spaces++;
                    string rest = line.Substring(spaces);
                    if (rest.StartsWith("- ") || rest.StartsWith("* "))
                    {
                        int level = spaces / 2;
                        int indent = 300 + level * 300;
                        string bullet = level == 0 ? @"\'95" : "-";
                        sb.Append(@"\pard\li").Append(indent).Append(@"\fi-200\sa30\cf1\fs19 ")
                          .Append(bullet).Append("  ").Append(Esc(rest.Substring(2))).Append(@"\par");
                    }
                    else
                    {
                        sb.Append(@"\pard\sa60\cf4\fs19 ").Append(Esc(rest)).Append(@"\par");
                    }
                }
                first = false;
            }
            sb.Append("}");
            return sb.ToString();
        }

        static string C(Color c)
        {
            return @"\red" + c.R + @"\green" + c.G + @"\blue" + c.B + ";";
        }

        static string Esc(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (ch == '\\' || ch == '{' || ch == '}') sb.Append('\\').Append(ch);
                else if (ch > 127) sb.Append(@"\u").Append((int)(short)ch).Append('?');
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
