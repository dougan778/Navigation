using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Navigation
{
    public class PausableWait : WebDriverWait
    {
        private IWebDriver input;
        private static TimeSpan DefaultSleepTimeout
        {
            get { return TimeSpan.FromMilliseconds(500); }
        }

        private TimeSpan timeout = DefaultSleepTimeout;
        private TimeSpan sleepInterval = DefaultSleepTimeout;
        private Action checkPause;

        public PausableWait(IWebDriver driver, TimeSpan timeout, Action checkPause) : base(driver, timeout)
        {
            this.input = driver;
            this.checkPause = checkPause;
            this.timeout = timeout;
        }

        /// <summary>
        /// Note that the time waited won't be completely accurate because it doesn't account for the time to check the condition.
        /// The regular WebDriverWait isn't perfectly accurate either, though... but this one is less accurate.
        /// </summary>
        public TResult UntilPausable<TResult>(Func<IWebDriver, TResult> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException("condition", "condition cannot be null");
            }

            var resultType = typeof(TResult);
            if ((resultType.IsValueType && resultType != typeof(bool)) || !typeof(object).IsAssignableFrom(resultType))
            {
                throw new ArgumentException("Can only wait on an object or boolean response, tried to use type: " + resultType.ToString(), "condition");
            }
            var intervalms = sleepInterval.TotalMilliseconds;
            var timeoutms = timeout.TotalMilliseconds;
            var iterations = timeoutms / intervalms;
            if (timeoutms % intervalms > 0)
            {
                iterations++;
            }
            Exception lastException = null;
            for (var i = 0; i < iterations; i++)
            {
                try
                {
                    checkPause();
                    var result = condition(this.input);
                    if (resultType == typeof(bool))
                    {
                        var boolResult = result as bool?;
                        if (boolResult.HasValue && boolResult.Value)
                        {
                            return result;
                        }
                    }
                    else
                    {
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (BreakException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                Thread.Sleep(this.sleepInterval);
            }
            string timeoutMessage = string.Format(CultureInfo.InvariantCulture, "Timed out after about {0} seconds", this.timeout.TotalSeconds);
            this.ThrowTimeoutException(timeoutMessage, lastException);
            return default(TResult); // unreachable.
        }

        public class BreakException : Exception
        {
            public BreakException() : base("Pausable wait has been terminated.") { }
            public BreakException(string message) : base(message) { }
        }
    }
}
