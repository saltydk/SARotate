using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Linuxtesting
{
    public static class ShellHelper
    {
        public static Task<string> Bash(this string cmd)
        {
            var source = new TaskCompletionSource<string>();
            var escapedArgs = cmd.Replace("\"", "\\\"");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };
            process.Exited += (sender, args) =>
            {
                if (process.ExitCode == 0)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var result = $"STDOUT:{output}";
                    source.SetResult(result);
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();

                    var result = $"STDERR:{error}";

                    source.SetResult(result);
                }

                process.Dispose();
            };

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                source.SetException(e);
            }

            return source.Task;
        }
    }
}
