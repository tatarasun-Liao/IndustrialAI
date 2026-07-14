using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialAI.Core.Logging
{
    //通过委托与泛型的配合封装重试逻辑
    public static class RetryHelper
    {
        public static async ValueTask RetryAsync(Func<ValueTask> operation, int maxAttempts = 3, CancellationToken cancellationToken = default)
        {
            int attempt = 0;

            while (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await operation();

                    return;
                }
                catch (Exception) when (attempt < maxAttempts - 1)
                {
                    attempt++;

                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));

                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }
}
