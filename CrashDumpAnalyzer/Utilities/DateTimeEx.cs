namespace CrashDumpAnalyzer.Utilities
{
    static class DateTimeEx
    {
        public static string AsTimeAgo(this DateTime dateTime)
        {
            TimeSpan timeSpan = DateTime.Now.Subtract(dateTime);

            return timeSpan.TotalSeconds switch
            {
                < 1 => $"{timeSpan.Seconds} seconds ago",
                <= 1 => $"{timeSpan.Seconds} second ago",
                <= 60 => $"{timeSpan.Seconds} seconds ago",

                _ => timeSpan.TotalMinutes switch
                {
                    <= 1 => "about a minute ago",
                    < 60 => $"about {timeSpan.Minutes} minutes ago",
                    _ => timeSpan.TotalHours switch
                    {
                        <= 1 => "about an hour ago",
                        < 24 => $"about {timeSpan.Hours} hours ago",
                        _ => timeSpan.TotalDays switch
                        {
                            <= 1 => "yesterday",
                            <= 30 => $"about {timeSpan.Days} days ago",

                            <= 60 => "about a month ago",
                            < 365 => $"about {timeSpan.Days / 30} months ago",

                            <= 365 * 2 => "about a year ago",
                            _ => $"about {timeSpan.Days / 365} years ago"
                        }
                    }
                }
            };
        }
    }
}
