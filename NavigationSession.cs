using log4net;
using Navigation.Exceptions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Linq;

namespace Navigation
{
    public class NavigationSession
    {
        public delegate void ReportStatusDelegate(string status);

        public ReportStatusDelegate LogDiagnostic = (string s) => { };
        public ReportStatusDelegate ReportStatusCall = (string s) => { };

        public bool ManuallyStopped { get; set; }
        public void ReportStatus(string status)
        {
            LogDiagnostic($"Status Change: {status}");
            ReportStatusCall(status);
        }

        public static int? MaxClosingSessions { get; set; } = null;
        protected static HashSet<NavigationSession> ClosingSessions = new HashSet<NavigationSession>();

        static NavigationSession()
        {
            ClearOldProxyExtensions();
        }

        private static void ClearOldProxyExtensions()
        {
            try
            {
                var directories = Directory.GetDirectories("proxy_extension");
                foreach (var directory in directories)
                {
                    try
                    {
                        Directory.Delete(directory, true);
                    }
                    catch
                    {

                    }
                }
                DirectoryInfo di = new DirectoryInfo("proxy_extension");
                FileInfo[] files = di.GetFiles("*.zip")
                     .Where(p => p.Extension == ".zip").ToArray();
                foreach(var file in files)
                {
                    try
                    {
                        file.Attributes = FileAttributes.Normal;
                        File.Delete(file.FullName); 
                    }
                    catch
                    {

                    }
                }
            }
            catch 
            {
            }
        }

        public static object chromeLock = new object();
        public bool Incognito { get; set; }
        public bool DisableImages { get; set; }
        public string UserAgent { get; set; }
        public string WindowSize { get; set; }
        public bool Headless { get; set; }
        public string Proxy { get; set; }
        public string ProxyUserName { get; set; } = null;
        public string ProxyPassword { get; set; } = null;
        public string ProfileLocation { get; set; }
        public bool CloseOnComplete { get; set; } = true;

        /// <summary>
        /// If CloseOnComplete is true, this will toggle whether or not it is synchronous.
        /// </summary>
        public bool CloseOnCompleteAsync { get; set; } = true;
        public bool DisableExtensions { get; set; }
        /// <summary>
        /// Hopefully removes the navigation.webdriver thing that can be used for bot detection
        /// https://github.com/GoogleChrome/chrome-launcher/blob/master/docs/chrome-flags-for-tools.md#--enable-automation
        /// </summary>
        public bool DisableAutomationFlags { get; set; }

        public enum RunStates
        {
            Running,
            Paused,
            Stopped
        }
        private object _runStateLock = new object();
        public RunStates RunState { get; private set; }

        public Action OnStopping;

        protected ChromeDriver _browser;

        public bool WindowIsOpen()
        {
            if (_browser == null) { return false; }
            try
            {
                var b = _browser.FindElements(By.XPath("//a"));
            }
            catch (WebDriverException ex)
            {
                if (ex.Message.Contains("chrome not reachable"))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// You can use this to universally adjust timeouts, for example if you are dealing with a website that is experiencing slowness issues
        /// and want to wait twice as long for everything.
        /// </summary>
        public decimal TimeoutCoefficient { get; set; } = 1M;

        protected List<NavigationStep> _steps = new List<NavigationStep>();
        public IReadOnlyCollection<NavigationStep> Steps { get { return _steps; } }
        public CancellationTokenSource CancellationSource = new CancellationTokenSource();
        public void AddStep(NavigationStep step)
        {
            _steps.Add(step);
        }

        public void AddStepAfter(NavigationStep target, NavigationStep toAdd)
        {
            var index = _steps.IndexOf(target);
            _steps.Insert(index + 1, toAdd);
        }
        public async Task BeginNavigation()
        {
            _browser = GetBrowser();

            await Task.Run(() =>
            {
                ReportStatus("Beginning Navigation Session");
                lock (_runStateLock)
                {
                    RunState = RunStates.Running;
                }
                int index = 0;
                try
                {
                    while (index < _steps.Count)
                    {
                        var step = _steps[index];
                        if (CancellationSource.Token.IsCancellationRequested) { break; }
                        step.ReportStatus = status => this.ReportStatus(status);
                        ReportStatus($"Starting task: {step.Description}");
                        step.Browser = _browser;
                        step.LogDiagnostic = LogDiagnostic;
                        try
                        {
                            step.TimeoutCoefficient = TimeoutCoefficient;
                            try
                            {
                                step.Execute(CancellationSource.Token, this);
                            }
                            catch (OperationCanceledException ex)
                            {
                                if (ex.CancellationToken != CancellationSource.Token)
                                {
                                    throw;
                                }
                                if (CancellationSource.Token.IsCancellationRequested) { break; }
                            }
                        }

                        catch (Exception ex)
                        {
                            if (RunState != RunStates.Stopped)
                            {
                                throw;
                            }
                            else
                            {
                                LogDiagnostic($"An error was encountered after the session stopped running: {ex.ToString()}");
                            }
                        }
                        index++;
                    }
                }
                catch(Exception ex)
                {
                    if (CloseOnComplete)
                    {
                        throw;
                    }
                    else
                    {
                        ReportStatus("An error happpened that will stop the session script.  The window will remain open.");
                        LogDiagnostic(ex.ToString());
                        throw;
                    }
                }
                if (CloseOnComplete)
                {
                    if (CloseOnCompleteAsync)
                    {
                        Task.Run(StopAsync);
                    }
                    else
                    {
                        Stop();
                    }
                }
            });
        }

        public static string GetManifestJSON()
        {
            var manifest_json = @"
            {
            ""version"": ""1.0.0"",
            ""manifest_version"": 2,
            ""name"": ""Chrome Proxy"",
            ""permissions"": [
                ""proxy"",
                ""tabs"",
                ""unlimitedStorage"",
                ""storage"",
                ""<all_urls>"",
                ""webRequest"",
                ""webRequestBlocking""
            ],
            ""background"": {
                ""scripts"": [""background.js""]
            },
            ""minimum_chrome_version"":""22.0.0""
            }
            ";
            return manifest_json;
        }

        public static string GetBackgroundJS(string PROXY_HOST, string PROXY_PORT, string PROXY_USER, string PROXY_PASS)
        {
            var background_js = @"
                var config = {
                    mode: ""fixed_servers"",
                    rules:
                        {
                        singleProxy:
                            {
                                scheme: ""http"",
                                host: """ + PROXY_HOST + @""",
                                port: parseInt(" + PROXY_PORT + @")
                            },
                        bypassList:[""localhost""]
                        }
                };
              //  alert(1);
                chrome.proxy.settings.set({ value: config, scope: ""regular""}, function() { });

                function callbackFn(details)
                {
                    return {
                    authCredentials:
                        {
                            username: """ + PROXY_USER + @""",
                            password: """ + PROXY_PASS + @"""
                        }
                    };
                }

                chrome.webRequest.onAuthRequired.addListener(
                        callbackFn,
                            { urls:[""<all_urls>""]},
                            ['blocking']
                    );
            ";
            return background_js;
        }

        public static object ProxyDirectoryLock = new object();

        public static void AddProxyExtensionToOptions(string proxyandport, string user, string password, ChromeOptions options, ReportStatusDelegate LogDiagnostic = null)
        {
            lock (ProxyDirectoryLock)
            {
                if (!Directory.Exists("proxy_extension"))
                {
                    Directory.CreateDirectory("proxy_extension");
                }
            }

            // Create extension.
            var extensionGuid = Guid.NewGuid();
            var extensionDirectory = @"proxy_extension\" + extensionGuid;

            LogDiagnostic?.Invoke("Creating extension directory: " + extensionDirectory);
            Directory.CreateDirectory(extensionDirectory);

            var split = proxyandport.Split(':');
            File.WriteAllText(extensionDirectory + @"\manifest.json", GetManifestJSON());
            File.WriteAllText(extensionDirectory + @"\background.js", GetBackgroundJS(split[0], split[1], user, password));
            var zipName = $"extension{extensionGuid}.zip";
            ZipFile.CreateFromDirectory(extensionDirectory, @"proxy_extension\" + zipName);
            File.Delete(extensionDirectory + @"\manifest.json");
            File.Delete(extensionDirectory + @"\background.js");

            options.AddExtension(@"proxy_extension\" + zipName);
        }

        protected ChromeDriver GetBrowser()
        {
            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--ignore-certificate-errors");

            if (Incognito && (Proxy == null || ProxyUserName == null))
            {
                options.AddArgument("--incognito");
            }
            if (Proxy != null)
            {
                ReportStatus("Using Proxy: " + Proxy);
                options.Proxy = new OpenQA.Selenium.Proxy() { HttpProxy = Proxy, SslProxy = Proxy };

                if (ProxyUserName != null)
                {
                    LogDiagnostic($"Using custom extension to authenticate with proxy. Username: {ProxyUserName} Password: {ProxyPassword}");
                    AddProxyExtensionToOptions(Proxy, ProxyUserName, ProxyPassword, options, LogDiagnostic);
                }
            }

            if (!string.IsNullOrEmpty(UserAgent)) options.AddArgument($"--user-agent={UserAgent}");
            if (DisableImages) options.AddUserProfilePreference("profile.default_content_setting_values.images", 2); // Disable images.
            options.AddArgument("--disable-geolocation");

            if (ProxyUserName == null) // Some, maybe all, of these don't work when we are using the proxy extension.
            {
                if (Headless) options.AddArgument("--headless");
                if (DisableAutomationFlags)
                {
                     options.AddExcludedArgument("enable-automation");
                }
                
                if (DisableExtensions)
                {
                    options.AddArgument("--disable-extensions");
                }
            }

            options.PageLoadStrategy = PageLoadStrategy.None;
            if (!string.IsNullOrEmpty(WindowSize)) options.AddArgument($"----window-size={WindowSize}");
            if (!string.IsNullOrEmpty(ProfileLocation))
            {
                if (!Directory.Exists(ProfileLocation))
                {
                    Directory.CreateDirectory(ProfileLocation);
                }
                options.AddArgument("user-data-dir=" + Directory.GetCurrentDirectory() + @"\" + ProfileLocation);
            }
            
            ChromeDriver browser = null;
            lock (chromeLock) // Because instantiating chromedriver twice at once can deadlock.
            {
                var chromeDriverService = ChromeDriverService.CreateDefaultService();
                chromeDriverService.HideCommandPromptWindow = true;
                try
                {
                    browser = new ChromeDriver(chromeDriverService, options);
                }
                catch (WebDriverException ex)
                {
                    if (ex.Message.Contains("user data directory is already in use"))
                    {
                        throw new ProfileDirectoryLockedException("Unable to access profile.  This is usually caused by an old version of Chrome using this account that is still hanging open.", ex);
                    }

                    if (ex.Message.Contains("Chrome failed to start: crashed"))
                    {
                        throw new ChromeFailedToStartException("Chrome failed to start.", ex);
                    }

                    if (ex.Message.Contains("cannot parse internal JSON template") && !string.IsNullOrEmpty(ProfileLocation))
                    {
                        throw new ChromeProfileCorruptedException("Failed to load the specified Chrome profile.  It is most likely corrupted and needs to be recreated.  Profile Location: " + ProfileLocation, ex);
                    }
                    throw;
                }
            }

            return browser;
        }

        public class ChromeFailedToStartException : System.Exception
        {
            public ChromeFailedToStartException(string message, Exception ex) : base(message, ex) { }
        }
        public class ChromeProfileCorruptedException : System.Exception
        {
            public ChromeProfileCorruptedException(string message, Exception ex) : base(message, ex) { }
        }
        public string GetCurrentHTML()
        {
            return _browser?.PageSource ?? "";
        }

        public Screenshot GetScreenshot()
        {
            _browser?.Manage().Window.Maximize();
            var result = _browser?.GetScreenshot();
            try
            {
                if (WindowSize != null)
                {
                    var sizes = WindowSize.Split(',');
                    var size = new System.Drawing.Size(int.Parse(sizes[0]), int.Parse(sizes[1]));
                    _browser.Manage().Window.Size = size;
                }
            }
            catch { }
            return result;
        }

        public void Stop(bool manual = false)
        {
            lock (_runStateLock)
            {
                if (RunState != RunStates.Stopped)
                {
                    
                    RunState = RunStates.Stopped;
                    CancellationSource?.Cancel();
                    DoBrowserClose();
                    if (OnStopping != null)
                    {
                        OnStopping();
                    }

                    ManuallyStopped = manual;
                }
            }
        }

        protected void DoBrowserClose()
        {
            if (MaxClosingSessions != null && MaxClosingSessions > 0)
            {
                try
                {
                    while (true)
                    {
                        lock (ClosingSessions)
                        {
                            if (ClosingSessions.Count() < MaxClosingSessions)
                            {
                                ClosingSessions.Add(this);
                                break;
                            }
                            else
                            {
                                LogDiagnostic("Waiting for other sessions to finish closing.");
                            }
                        }

                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }

                    try
                    {
                        _browser?.Close();
                        _browser?.Quit();
                    }
                    catch { }
                }
                finally
                {
                    lock (ClosingSessions)
                    {
                        if (ClosingSessions.Contains(this))
                        {
                            ClosingSessions.Remove(this);
                        }
                    }
                }
            }
            else
            {
                try
                {
                    _browser?.Close();
                    _browser?.Quit();
                }
                catch { }
            }
        }

        public async Task StopAsync()
        {
            bool continueStop = false;
            lock (_runStateLock)
            {
                if (RunState != RunStates.Stopped)
                {
                    RunState = RunStates.Stopped;
                    continueStop = true;
                    CancellationSource?.Cancel();
                }
            }

            if (continueStop)
            {
                await Task.Run(() =>
                {
                    DoBrowserClose();
                    if (OnStopping != null)
                    {
                        OnStopping();
                    }
                });
            }
                    
        }

        public void Pause()
        {
            lock (_runStateLock)
            {
                if (RunState == RunStates.Running)
                {
                    RunState = RunStates.Paused;
                }
            }
        }

        public void Unpause()
        {
            lock (_runStateLock)
            {
                if (RunState == RunStates.Paused)
                {
                    RunState = RunStates.Running;
                }
            }
        }

        public void BringToFront()
        {
            _browser?.SwitchTo().Window(_browser.CurrentWindowHandle);
        }
    }
}
