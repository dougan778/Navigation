using log4net;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Navigation
{
    public abstract class NavigationStep
    {
        public NavigationSession.ReportStatusDelegate ReportStatus;
        public NavigationSession.ReportStatusDelegate LogDiagnostic;

        public decimal TimeoutCoefficient { get; set; }

        public ChromeDriver Browser { get; set; }
        protected CancellationToken _token { get; private set; }
        protected NavigationSession _session { get; private set; }
        public virtual void Execute(CancellationToken token, NavigationSession session = null)
        {
            _token = token;
            _session = session;
            try
            {
                PerformExecute();
            }
            finally
            {
                _token = default(CancellationToken);
            }
        }
        protected abstract void PerformExecute();
        public abstract string Description { get; }

        protected void CheckPause()
        {
            var paused = false;
            while (!_token.IsCancellationRequested && (_session?.RunState ?? NavigationSession.RunStates.Running) == NavigationSession.RunStates.Paused)
            {
                if (!paused)
                {
                    paused = true;
                    LogDiagnostic("Pausing.");
                }
                _token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            }
            if (paused)
            {
                LogDiagnostic("Unpausing.");
            }

            if (_token.IsCancellationRequested)
            {
                throw new OperationCanceledException("Navigation Cancelled.", _token);
            }
        }

        protected void SendKeysSlowByXPath(string xpath, string keys, int millsecondDelay = 3)
        {
            for (var i = 0; i < 11; i++)
            {
                CheckPause();
                try
                {
                    LogDiagnostic($"Sending Keys slow. XPath: {xpath} Keys: {keys}");
                    var element = Browser.FindElement(By.XPath(xpath));
                    var keyChars = keys.ToCharArray();
                    foreach(var charr in keyChars)
                    {
                        CheckPause();
                        element.SendKeys(charr.ToString());
                        Sleep(millsecondDelay);
                    }
                    
                    break;
                }
                catch (InvalidOperationException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("InvalidOperationException encountered while sending keys slow.  Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("InvalidOperationException encountered while sending keys slow.  Sleeping.");
                        Sleep(500);
                    }
                }
                catch (ElementNotVisibleException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while sending keys slow.  Throwing.");

                        throw;
                    }
                    else
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while sending keys slow.  Sleeping.");
                        Sleep(500);
                    }
                }
            }
        }

        public void GoToUrl(string url, bool waitForPageToLoad = true)
        {
            CheckPause();
            LogDiagnostic("Navigating to URL: " + url);
            Browser.Navigate().GoToUrl(url);
            if (waitForPageToLoad)
            {
                WaitForPageToLoad();
            }
        }

        public void SendKeysByXPath(string xpath, string keys, bool confidential = false, int attempts = 10)
        {
            CheckPause();
            SendKeysByXPathNoTabs(xpath, keys, confidential, attempts);
            SendKeysByXPathNoTabs(xpath, Keys.Tab, confidential, attempts);
        }

        protected void SendKeysByXPathNoTabs(string xpath, string keys, bool confidential = false, int attempts = 10)
        {
            for (var i = 0; i <= attempts; i++)
            {
                CheckPause();
                try
                {
                    const string confidentialDiagnostics = "CONFIDENTIAL";
                    LogDiagnostic($"Sending Keys. XPath: {xpath} Keys: {(confidential ? confidentialDiagnostics : keys)}");
                    Browser.FindElement(By.XPath(xpath)).SendKeys(keys);
                    break;
                }
                catch (InvalidOperationException)
                {
                    if (i == attempts)
                    {
                        LogDiagnostic("InvalidOperationException encountered while sending keys.  Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("InvalidOperationException encountered while sending keys.  Sleeping.");
                        Sleep(500);
                    }
                }
                catch (ElementNotVisibleException)
                {
                    if (i == attempts)
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while sending keys.  Throwing.");

                        throw;
                    }
                    else
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while sending keys.  Sleeping.");
                        Sleep(500);
                    }
                }
            }
        }
        protected void ClickButtonBySelector(string selector)
        {
            for (var i = 0; i < 11; i++)
            {
                CheckPause();
                try
                {
                    LogDiagnostic($"Clicking item by selector {selector}");
                    Browser.FindElement(By.CssSelector(selector)).Click();
                    break;
                }
                catch (InvalidOperationException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("InvalidOperationException encountered while clicking by selector. Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("InvalidOperationException encountered while clicking by selector. Sleeping.");
                        Sleep(500);
                    }
                }
                catch (ElementNotVisibleException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while clicking by selector. Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while clicking by selector. Sleeping.");
                        Sleep(500);
                    }
                }
            }
        }

        protected void ClickButtonByXPath(string xpath)
        {
            for (var i = 0; i < 11; i++)
            {
                CheckPause();
                try
                {
                    LogDiagnostic($"Clicking item by xpath {xpath}");
                    Browser.FindElement(By.XPath(xpath)).Click();
                    break;
                }
                catch (InvalidOperationException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("InvalidOperationException encountered while clicking by xpath. Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("InvalidOperationException encountered while clicking by xpath. Sleeping.");
                        Sleep(500);
                    }
                }
                catch (ElementNotVisibleException)
                {
                    if (i == 10)
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while clicking by xpath. Throwing.");
                        throw;
                    }
                    else
                    {
                        LogDiagnostic("ElementNotVisibleException encountered while clicking by xpath. Sleeping.");
                        Sleep(500);
                    }
                }
            }
        }

        public void Sleep(TimeSpan time)
        {
            Sleep(time.Ticks / 10000);
        }
        protected void Sleep(double milliseconds)
        {
            CheckPause();
            var effectiveMilliseconds = (double)(((decimal)milliseconds) * TimeoutCoefficient);
            var effectiveTime = TimeSpan.FromMilliseconds(effectiveMilliseconds);
            LogDiagnostic($"Sleeping {effectiveMilliseconds} ms");
            Thread.Sleep(effectiveTime);
            CheckPause();
        }

        public IWebElement GetElementByXPath(string xPath)
        {
            CheckPause();
            return Browser.FindElement(By.XPath(xPath));
        }

        public ReadOnlyCollection<IWebElement> GetElementsByXPath(string xPath)
        {
            CheckPause();
            return Browser.FindElements(By.XPath(xPath));
        }

        public virtual void WaitUntil<T>(Func<IWebDriver, T> until, string waitDescription, int delay = 10, string timeoutMessage = null)
        {
            CheckPause();
            var effectiveDelay = (double)(((decimal)delay) * TimeoutCoefficient);
            var wait = new PausableWait(Browser, TimeSpan.FromSeconds(effectiveDelay), CheckPause);
            LogDiagnostic($"Waiting: {waitDescription} Delay {effectiveDelay.ToString()}");
            try
            {
                CheckPause();
                wait.UntilPausable(until);
                CheckPause();
            }
            catch (WebDriverTimeoutException ex)
            {
                if (timeoutMessage == null)
                {
                    LogDiagnostic($"The wait timed out.  Exception will be thrown.");
                    throw;
                }
                else
                {
                    LogDiagnostic($"The wait timed out: {timeoutMessage}");
                    throw new Exception(timeoutMessage, ex);
                }
            }
            LogDiagnostic($"Wait condition met.");
        }

        protected virtual void WaitForXPathsToBeOnScreen(IEnumerable<string> xpaths, int delay = 10, string timeoutMessage = null)
        {
            CheckPause();
            WaitUntil(brw =>
            {
                foreach(string xpath in xpaths)
                {
                    if (brw.FindElements(By.XPath(xpath)).Count > 0)
                    {
                        LogDiagnostic("Found " + xpath);
                        return true;
                    }
                }

                LogDiagnostic("No xpaths were found on screen.");
                return false;
            }, $"Waiting for elements to be on screen. XPath: {string.Join(",", xpaths)}", delay, timeoutMessage);
        }

        protected virtual void WaitForXPathToBeOnScreen(string xpath, int delay = 10, string timeoutMessage = null)
        {
            CheckPause();
            WaitUntil(brw =>
            {
                var count = brw.FindElements(By.XPath(xpath)).Count;
                if (count > 0)
                {
                    return true;
                }
                else
                {
                    LogDiagnostic($"Expected element not found on screen yet.");
                    return false;
                }
            }, $"Waiting for element to be on screen. XPath: {xpath}", delay, timeoutMessage);
        }
        
        protected void WaitForPageToLoad()
        {
            CheckPause();
            WaitUntil(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"), "Waiting for page to load.");
            CheckPause();
        }

        public void RemoveElementFromScreenByClassName(string className)
        {
            CheckPause();
            ((IJavaScriptExecutor)Browser).ExecuteScript($"return document.getElementsByClassName('{className}')[0].remove();");
        }

        public void ClickElementIntoNewTabByXPath(string xPath)
        {
            LogDiagnostic("Control-Clicking element to open it in a new tab: " + xPath);
            IWebElement link = GetElementByXPath(xPath);

            // TODO maybe cache this.
            Actions newTab = new Actions(Browser);
            newTab.KeyDown(Keys.Control).Click(link).KeyUp(Keys.Control).Build().Perform();
        }
    }
}
