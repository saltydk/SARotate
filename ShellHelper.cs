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
                //logger.LogWarning(process.StandardError.ReadToEnd());
                //logger.LogInformation(process.StandardOutput.ReadToEnd());
                if (process.ExitCode == 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    var output = process.StandardOutput.ReadToEnd();
                    var result = "";
                    result += string.IsNullOrEmpty(error) ? "" : $"STDERR: {error} \n";
                    result += string.IsNullOrEmpty(output) ? "" : $"STDOUT: {output} \n"; ;

                    source.SetResult(result);
                }
                else
                {
                    var result = "";
                    result += $"STDERR: {process.StandardError.ReadToEnd()} \n";

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
