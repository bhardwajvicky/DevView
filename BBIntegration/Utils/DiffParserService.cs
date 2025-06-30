using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BBIntegration.Utils
{
    public class DiffParserService
    {
        private static readonly string[] CommentMarkers = { "//", "/*", "*", "*/", "#", "<!--", "-->" };

        public (int totalAdded, int totalRemoved, int codeAdded, int codeRemoved) ParseDiff(string diffContent)
        {
            var lines = diffContent.Split('\n');
            int totalAdded = 0, totalRemoved = 0, codeAdded = 0, codeRemoved = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("+++") || line.StartsWith("---") || line.StartsWith("diff --git") || line.StartsWith("index "))
                    continue;

                if (line.StartsWith("+"))
                {
                    totalAdded++;
                    if (!IsCommentOrWhitespace(line.Substring(1)))
                    {
                        codeAdded++;
                    }
                }
                else if (line.StartsWith("-"))
                {
                    totalRemoved++;
                    if (!IsCommentOrWhitespace(line.Substring(1)))
                    {
                        codeRemoved++;
                    }
                }
            }

            return (totalAdded, totalRemoved, codeAdded, codeRemoved);
        }

        private bool IsCommentOrWhitespace(string line)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                return true;
            }

            return CommentMarkers.Any(marker => trimmedLine.StartsWith(marker));
        }
    }
} 