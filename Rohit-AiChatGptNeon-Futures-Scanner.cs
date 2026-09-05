using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string AppName = "Neon Futures Candle Scanner — V7";
    private const string HistoryFile = "V7_PaperTrade_History.txt";

    // SECURITY: The scanner password is intentionally NOT stored in source code.
    // Set NEON_SCANNER_PASSWORD in the local environment before running.

    private const int CandleLimit = 200;
    private const int ScanDelaySeconds = 10;
    private const int MaxScanHistory = 30;
    private const int MaxJournalEntries = 50;
    private const int MaxRetryAttempts = 3;

    private static NotificationService? Notifications;
    private static FuturesBinanceClient? BinanceClient;

    private static async Task Main()
    {
        Console.Title = AppName;

        var apiStats = new ApiStats();

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "NeonFuturesScanner",
                "7.0"));

        Notifications =
            NotificationService.FromEnvironment(http);

        BinanceClient =
            FuturesBinanceClient.FromEnvironment(http);

        var symbols = new List<string>
           {
             "BTCUSDT",
             "ETHUSDT",
               "BNBUSDT",
              "SOLUSDT",
              "XRPUSDT",
               "ADAUSDT",
              "DOGEUSDT",
            "AVAXUSDT",
             "LINKUSDT",
             "DOTUSDT",
            "LTCUSDT",
            "TRXUSDT",
             "SUIUSDT",
            "TONUSDT",
            "PAXGUSDT"
          };

        var intervals = new List<string>
             {
               "15m",
               "30m",
               "1h",
               "4h",
               "1d"
                };

        string selectedSymbol = "BTCUSDT";
        string selectedTimeframe = "15m";

        bool scannerEnabled = false;
        bool firstRun = true;

        var indicators = new IndicatorSettings();

        var scanHistory = new List<ScanHistoryRecord>();
        
        // ==========================================
// PAPER ACCOUNT — FAKE MONEY ONLY
// ==========================================

var paperAccount = new PaperAccount
{
    StartingBalance = 10_000_000m,
    Balance = 10_000_000m
};

        // Full persistent history.
        var paperHistory = LoadV7History(HistoryFile);
        LoadPaperAccount(HistoryFile, paperAccount, paperHistory);

        // Display/session journal is intentionally limited.
        var paperJournal = paperHistory
            .TakeLast(MaxJournalEntries)
            .ToList();

        Console.Clear();
        PrintHeader();

        Console.WriteLine();
        Console.WriteLine("PASSWORD REQUIRED");
        Console.WriteLine();

        Console.Write("Password: ");
        string enteredPassword = Console.ReadLine() ?? string.Empty;

        string? expectedPassword =
            Environment.GetEnvironmentVariable(
                "NEON_SCANNER_PASSWORD");

        if (string.IsNullOrWhiteSpace(expectedPassword))
        {
            Console.WriteLine();
            Console.WriteLine("CONFIGURATION ERROR");
            Console.WriteLine(
                "NEON_SCANNER_PASSWORD is not configured.");
            Console.WriteLine(
                "Set the environment variable and run the scanner again.");
            return;
        }

        if (!SecurePasswordEquals(
                enteredPassword,
                expectedPassword))
        {
            Console.WriteLine();
            Console.WriteLine("ACCESS DENIED");
            Console.WriteLine("Wrong password.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("ACCESS GRANTED");

        Thread.Sleep(700);

        while (true)
        {
            try
            {
                Console.Clear();
                PrintHeader();

                PrintMainStatus(
                    selectedSymbol,
                    selectedTimeframe,
                    scannerEnabled,
                    indicators);

                if (firstRun)
                {
                    Console.WriteLine();
                    Console.WriteLine("------------------------------------------");
                    Console.WriteLine("INITIAL MARKET SELECTION");
                    Console.WriteLine("------------------------------------------");

                    SelectCoin(
                        symbols,
                        ref selectedSymbol);

                    SelectTimeframe(
                        intervals,
                        ref selectedTimeframe);

                    firstRun = false;
                    scannerEnabled = true;

                    continue;
                }

                PrintControls();

                if (!scannerEnabled)
                {
                    Console.WriteLine();
                    Console.WriteLine("------------------------------------------");
                    Console.WriteLine("SCANNER OFF");
                    Console.WriteLine("------------------------------------------");
                    Console.WriteLine();
                    Console.WriteLine(
                        "Automatic scanning is disabled.");
                    Console.WriteLine();

                    ConsoleKey key =
                        Console.ReadKey(true).Key;

                    switch (key)
                    {
                        case ConsoleKey.I:
                            IndicatorSettingsMenu(indicators);
                            break;

                        case ConsoleKey.O:
                            scannerEnabled = true;
                            break;

                        case ConsoleKey.S:
                            await RunScanAsync(
                                http,
                                selectedSymbol,
                                selectedTimeframe,
                                CandleLimit,
                                scanHistory,
                                paperJournal,
                                paperHistory,
                                indicators,
                                apiStats, paperAccount);
                            break;

                        case ConsoleKey.C:
                            SelectCoin(
                                symbols,
                                ref selectedSymbol);
                            break;

                        case ConsoleKey.T:
                            SelectTimeframe(
                                intervals,
                                ref selectedTimeframe);
                            break;

                        case ConsoleKey.Q:
                            return;
                    }

                    continue;
                }

                await RunScanAsync(
                    http,
                    selectedSymbol,
                    selectedTimeframe,
                    CandleLimit,
                    scanHistory,
                    paperJournal,
                    paperHistory,
                    indicators,
                    apiStats,
                     paperAccount);

                Console.WriteLine();
                Console.WriteLine("------------------------------------------");
                Console.WriteLine("SCANNER CONTROL");
                Console.WriteLine("------------------------------------------");
                Console.WriteLine("O = KEEP ON");
                Console.WriteLine("F = TURN OFF");
                Console.WriteLine("S = SCAN NOW");
                Console.WriteLine("C = CHANGE COIN");
                Console.WriteLine("T = CHANGE TIMEFRAME");
                Console.WriteLine("I = INDICATOR SETTINGS");
                Console.WriteLine("Q = EXIT");
                Console.WriteLine();
                Console.WriteLine(
                    $"Next automatic scan in {ScanDelaySeconds} seconds...");

                bool interrupted = false;

                for (int i = 0;
                     i < ScanDelaySeconds * 10;
                     i++)
                {
                    await Task.Delay(100);

                    if (!Console.KeyAvailable)
                        continue;

                    ConsoleKey key =
                        Console.ReadKey(true).Key;

                    switch (key)
                    {
                        case ConsoleKey.F:
                            scannerEnabled = false;
                            interrupted = true;
                            break;

                        case ConsoleKey.Q:
                            return;

                        case ConsoleKey.S:
                            interrupted = true;
                            break;

                        case ConsoleKey.C:
                            SelectCoin(
                                symbols,
                                ref selectedSymbol);

                            interrupted = true;
                            break;

                        case ConsoleKey.T:
                            SelectTimeframe(
                                intervals,
                                ref selectedTimeframe);

                            interrupted = true;
                            break;

                        case ConsoleKey.I:
                            IndicatorSettingsMenu(indicators);
                            interrupted = true;
                            break;

                        case ConsoleKey.O:
                            scannerEnabled = true;
                            break;
                    }

                    if (interrupted)
                        break;
                }
            }
            catch (HttpRequestException ex)
            {
                ShowError(
                    "FUTURES MARKET DATA ERROR",
                    ex.Message);
            }
            catch (TaskCanceledException)
            {
                ShowError(
                    "MARKET DATA REQUEST TIMEOUT",
                    "The Binance Futures request timed out.");
            }
            catch (Exception ex)
            {
                ShowError(
                    "UNEXPECTED ERROR",
                    ex.Message);
            }
        }
    }

    private static bool SecurePasswordEquals(
        string entered,
        string expected)
    {
        byte[] left =
            Encoding.UTF8.GetBytes(entered);

        byte[] right =
            Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(
            left,
            right);
    }

    private static void PrintHeader()
    {
        Console.WriteLine("==========================================");
        Console.WriteLine(" NEON FUTURES CANDLE SCANNER — V7");
        Console.WriteLine("==========================================");
    }

    private static void PrintMainStatus(
        string symbol,
        string timeframe,
        bool scannerEnabled,
        IndicatorSettings indicators)
    {
        Console.WriteLine();
        Console.WriteLine("MODE : PAPER / ANALYSIS ONLY");
        Console.WriteLine("DATA SOURCE : LIVE BINANCE FUTURES DATA");
        Console.WriteLine();

        Console.WriteLine($"COIN : {symbol}");
        Console.WriteLine($"PRIMARY TF : {timeframe}");

        Console.WriteLine(
            $"SCANNER : {(scannerEnabled ? "ON" : "OFF")}");

        Console.WriteLine(
            $"INDICATORS : {indicators.GetSummary()}");
    }

    private static void PrintControls()
    {
        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("CONTROLS");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("O = SCANNER ON");
        Console.WriteLine("F = SCANNER OFF");
        Console.WriteLine("S = SCAN NOW");
        Console.WriteLine("C = CHANGE COIN");
        Console.WriteLine("T = CHANGE TIMEFRAME");
        Console.WriteLine("I = INDICATOR SETTINGS");
        Console.WriteLine("Q = EXIT");
    }

    private static void IndicatorSettingsMenu(
        IndicatorSettings settings)
    {
        while (true)
        {
            Console.Clear();
            PrintHeader();

            Console.WriteLine();
            Console.WriteLine("------------------------------------------");
            Console.WriteLine("INDICATOR SETTINGS");
            Console.WriteLine("------------------------------------------");

            Console.WriteLine(
                $"1. SMA    : {(settings.SmaEnabled ? "ON" : "OFF")}");

            Console.WriteLine(
                $"2. EMA    : {(settings.EmaEnabled ? "ON" : "OFF")}");

            Console.WriteLine(
                $"3. RSI    : {(settings.RsiEnabled ? "ON" : "OFF")}");

            Console.WriteLine(
                $"4. MACD   : {(settings.MacdEnabled ? "ON" : "OFF")}");

            Console.WriteLine(
                $"5. ATR    : {(settings.AtrEnabled ? "ON" : "OFF")}");

            Console.WriteLine(
                $"6. Volume : {(settings.VolumeEnabled ? "ON" : "OFF")}");

            Console.WriteLine();
            Console.WriteLine("0. BACK");
            Console.WriteLine();

            Console.Write("Select indicator: ");

            string input =
                Console.ReadLine()?
                    .Trim()
                    ?? string.Empty;

            switch (input)
            {
                case "1":
                    settings.SmaEnabled =
                        !settings.SmaEnabled;
                    break;

                case "2":
                    settings.EmaEnabled =
                        !settings.EmaEnabled;
                    break;

                case "3":
                    settings.RsiEnabled =
                        !settings.RsiEnabled;
                    break;

                case "4":
                    settings.MacdEnabled =
                        !settings.MacdEnabled;
                    break;

                case "5":
                    settings.AtrEnabled =
                        !settings.AtrEnabled;
                    break;

                case "6":
                    settings.VolumeEnabled =
                        !settings.VolumeEnabled;
                    break;

                case "0":
                    return;
            }
        }
    }

    private static void ShowError(
        string title,
        string message)
    {
        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine(title);
        Console.WriteLine("------------------------------------------");
        Console.WriteLine(message);
        Console.WriteLine();
        Console.WriteLine("Press any key to continue.");
        Console.ReadKey(true);
    }

    private static void SelectCoin(
        List<string> symbols,
        ref string selectedSymbol)
    {
        Console.Clear();
        PrintHeader();

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("SELECT COIN");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine();

        for (int i = 0; i < symbols.Count; i++)
        {
            Console.WriteLine(
                $"{i + 1}. {symbols[i]}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "You can also type another Binance Futures symbol.");
        Console.WriteLine();

        Console.Write($"Coin [{selectedSymbol}]: ");

        string input =
            Console.ReadLine()?
                .Trim()
                .ToUpperInvariant()
                ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(input))
        {
            if (int.TryParse(
                    input,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int number) &&
                number >= 1 &&
                number <= symbols.Count)
            {
                selectedSymbol =
                    symbols[number - 1];
            }
            else
            {
                selectedSymbol =
                    NormalizeSymbol(input);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Selected coin: {selectedSymbol}");

        Thread.Sleep(600);
    }

    private static void SelectTimeframe(
        List<string> intervals,
        ref string selectedTimeframe)
    {
        Console.Clear();
        PrintHeader();

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("SELECT TIMEFRAME");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine();

        for (int i = 0; i < intervals.Count; i++)
        {
            Console.WriteLine(
                $"{i + 1}. {intervals[i]}");
        }

        Console.WriteLine();
        Console.Write(
            $"Timeframe [{selectedTimeframe}]: ");

        string input =
            Console.ReadLine()?
                .Trim()
                .ToLowerInvariant()
                ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(input))
        {
            if (int.TryParse(
                    input,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int number) &&
                number >= 1 &&
                number <= intervals.Count)
            {
                selectedTimeframe =
                    intervals[number - 1];
            }
            else if (intervals.Contains(
                         input,
                         StringComparer.OrdinalIgnoreCase))
            {
                selectedTimeframe = input;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Unsupported timeframe.");

                Thread.Sleep(700);
                return;
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Selected timeframe: {selectedTimeframe}");

        Thread.Sleep(600);
    }

    private static string NormalizeSymbol(
        string input)
    {
        var symbol =
            new string(
                input
                    .Where(char.IsLetterOrDigit)
                    .ToArray());

        return symbol.ToUpperInvariant();
    }

    private static async Task RunScanAsync(
        HttpClient http,
        string symbol,
        string primaryTimeframe,
        int candleLimit,
        List<ScanHistoryRecord> scanHistory,
        List<PaperTrade> paperJournal,
        List<PaperTrade> paperHistory,
        IndicatorSettings indicators,
        ApiStats apiStats , PaperAccount paperAccount)
    {
        Console.Clear();
        PrintHeader();

        Console.WriteLine();
        Console.WriteLine(
            "MODE            : PAPER / ANALYSIS ONLY");

        Console.WriteLine(
            "DATA SOURCE     : LIVE BINANCE FUTURES DATA");

        Console.WriteLine();
        Console.WriteLine(
            $"COIN            : {symbol}");

        Console.WriteLine(
            $"PRIMARY TF      : {primaryTimeframe}");

        Console.WriteLine(
            $"INDICATORS      : {indicators.GetSummary()}");

        Console.WriteLine();

        var timeframes = new[]
          {
           "15m",
           "30m",
           "1h",
           "4h",
           "1d"
          };

        var analyses =
            new List<TimeframeAnalysis>();

        Console.WriteLine("------------------------------------------");
        Console.WriteLine("SINGLE SCAN CYCLE");
        Console.WriteLine("------------------------------------------");
        Console.WriteLine();

        foreach (string timeframe in timeframes)
        {
            Console.WriteLine(
                $"Fetching {symbol} / {timeframe} ...");

            try
            {
                List<Candle> candles =
                    await GetLiveCandlesAsync(
                        http,
                        symbol,
                        timeframe,
                        candleLimit,
                        apiStats);

                if (candles.Count < 30)
                {
                    Console.WriteLine(
                        $"  {timeframe}: insufficient valid candles.");

                    continue;
                }

                TimeframeAnalysis analysis =
                    AnalyzeTimeframe(
                        candles,
                        timeframe,
                        indicators);

                analyses.Add(analysis);

                Console.WriteLine(
                    $"  {timeframe}: {candles.Count} valid candles");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine(
                    $"  {timeframe}: HTTP ERROR - {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine(
                    $"  {timeframe}: TIMEOUT");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  {timeframe}: ERROR - {ex.Message}");
            }
        }

        if (analyses.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "NO VALID MARKET DATA RECEIVED.");
            Console.WriteLine();
            return;
        }

        TimeframeAnalysis primary =
            analyses.FirstOrDefault(
                a => a.Timeframe.Equals(
                    primaryTimeframe,
                    StringComparison.OrdinalIgnoreCase))
            ?? analyses[0];

        ApplyMultiTimeframeMetrics(analyses);

        foreach (TimeframeAnalysis analysis in analyses)
        {
            analysis.Confidence =
                CalculateConfidence(
                    analysis,
                    indicators);
        }

        string overallSignal =
            CalculateMultiTimeframeSignal(
                analyses);

        decimal overallConfidence =
            CalculateOverallConfidence(
                analyses);

        bool isDuplicate =
            IsDuplicatePrimaryAnalysis(
                symbol,
                primary,
                scanHistory);

        if (!isDuplicate)
        {
            scanHistory.Add(
                new ScanHistoryRecord
                {
                    Timestamp = DateTime.UtcNow,
                    CandleTimestamp = primary.Timestamp,
                    Coin = symbol,
                    PrimaryTimeframe = primary.Timeframe,
                    Price = primary.CurrentPrice,
                    Signal = GetDisplaySignal(
                        primary),
                    Confidence = primary.Confidence
                });

            TrimList(
                scanHistory,
                MaxScanHistory);
        }

        if (!isDuplicate)
        {
            await Notifications!.SendSignalAsync(
                symbol,
                primary);
        }

        // First update existing trades using candles after their entry.
        UpdatePaperJournal(
         symbol,
          primary,
         paperHistory,
         paperAccount);

        if (!isDuplicate)
        {
            await TryCreatePaperSetup(
                symbol,
                primary,
                paperHistory,
                paperJournal,
                paperAccount,
                indicators);

            TrimList(
                paperJournal,
                MaxJournalEntries);
        }

        // Save after updates/entries so persistent history contains
        // the latest state.
        SaveV7History(
            paperHistory,
            paperAccount);

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("MARKET STATE");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"COIN            : {symbol}");

        Console.WriteLine(
            $"PRIMARY TF      : {primary.Timeframe}");

        Console.WriteLine(
            $"CURRENT PRICE   : {FormatPrice(primary.CurrentPrice)} USDT");

        Console.WriteLine(
            $"OVERALL STATE   : {overallSignal}");

        Console.WriteLine(
            $"OVERALL CONF.   : {overallConfidence:F0}/100");

        if (isDuplicate)
        {
            Console.WriteLine(
                "DUPLICATE       : SAME PRIMARY CANDLE ALREADY ANALYZED");
        }
        else
        {
            Console.WriteLine(
                "SCAN STATUS     : NEW PRIMARY CANDLE");
        }

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("API STATISTICS");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Requests        : {apiStats.TotalRequests}");

        Console.WriteLine(
            $"HTTP/API errors : {apiStats.ApiErrors}");

        Console.WriteLine(
            $"Timeouts        : {apiStats.ApiTimeouts}");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("TIMEFRAME SUMMARY");
        Console.WriteLine("------------------------------------------");

        foreach (TimeframeAnalysis analysis in analyses)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"[{analysis.Timeframe}]");

            Console.WriteLine(
                $"Trend           : {analysis.Trend}");

            Console.WriteLine(
                $"SMA 20          : {FormatValue(analysis.Sma)}");

            Console.WriteLine(
                $"EMA 20          : {FormatValue(analysis.Ema)}");

            Console.WriteLine(
                $"RSI 14          : {FormatValue(analysis.Rsi)}");

            Console.WriteLine(
                $"MACD Histogram  : {FormatValue(analysis.MacdHistogram)}");

            Console.WriteLine(
                $"ATR 14          : {FormatValue(analysis.Atr)}");

            Console.WriteLine(
                $"Volume          : {analysis.VolumeStatus}");

            Console.WriteLine(
                $"Support         : {FormatPrice(analysis.Support)}");

            Console.WriteLine(
                $"Resistance      : {FormatPrice(analysis.Resistance)}");

            Console.WriteLine(
                $"Signal          : {analysis.Signal}");

            Console.WriteLine(
                $"Confidence      : {analysis.Confidence:F0}/100");
        }

        int bullishCount =
            analyses.Count(
                a => a.Direction ==
                     MarketDirection.Bullish);

        int bearishCount =
            analyses.Count(
                a => a.Direction ==
                     MarketDirection.Bearish);

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("MULTI-TIMEFRAME CONFIRMATION");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Bullish TFs     : {bullishCount}");

        Console.WriteLine(
            $"Bearish TFs     : {bearishCount}");

        Console.WriteLine(
            $"Neutral TFs     : {analyses.Count - bullishCount - bearishCount}");

        Console.WriteLine(
            $"Timeframes      : {analyses.Count}/5");

        Console.WriteLine(
            $"Agreement       : {primary.MultiTimeframeAgreement}/{analyses.Count}");

        Console.WriteLine(
            $"Structure Agree. : {primary.StructuralAgreementCount}/{analyses.Count}");

        Console.WriteLine(
            $"HTF Supported   : {(primary.HigherTimeframeSupported ? "YES" : "NO")}");

        Console.WriteLine(
            $"MTF Conflict     : {(primary.MultiTimeframeConflict ? "YES" : "NO")}");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("PRIMARY TIMEFRAME DETAILS");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Candle Time     : {primary.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

        Console.WriteLine(
            $"Open            : {FormatPrice(primary.Open)}");

        Console.WriteLine(
            $"High            : {FormatPrice(primary.High)}");

        Console.WriteLine(
            $"Low             : {FormatPrice(primary.Low)}");

        Console.WriteLine(
            $"Close           : {FormatPrice(primary.Close)}");

        Console.WriteLine(
            $"Volume          : {FormatDecimal(primary.Volume)}");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("INDICATORS");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"SMA 20          : {FormatValue(primary.Sma)}");

        Console.WriteLine(
            $"EMA 20          : {FormatValue(primary.Ema)}");

        Console.WriteLine(
            $"RSI 14          : {FormatValue(primary.Rsi)}");

        Console.WriteLine(
            $"MACD            : {FormatValue(primary.Macd)}");

        Console.WriteLine(
            $"MACD Signal     : {FormatValue(primary.MacdSignal)}");

        Console.WriteLine(
            $"MACD Histogram  : {FormatValue(primary.MacdHistogram)}");

        Console.WriteLine(
            $"ATR 14          : {FormatValue(primary.Atr)}");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("SUPPORT / RESISTANCE");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Support         : {FormatPrice(primary.Support)}");

        Console.WriteLine(
            $"Resistance      : {FormatPrice(primary.Resistance)}");

        Console.WriteLine(
            $"Price Position  : {primary.PricePosition}");

        Console.WriteLine(
            $"Support Tested  : {(primary.SupportTested ? "YES" : "NO")}");

        Console.WriteLine(
            $"Support Reject. : {(primary.SupportRejection ? "YES" : "NO")}");

        Console.WriteLine(
            $"Resistance Test.: {(primary.ResistanceTested ? "YES" : "NO")}");

        Console.WriteLine(
            $"Resistance Reject: {(primary.ResistanceRejection ? "YES" : "NO")}");

        Console.WriteLine(
            $"Level Flip       : {primary.LevelFlip}");

        Console.WriteLine(
            $"Market Structure : {primary.MarketStructure}");

        Console.WriteLine(
            $"Structural Trend : {primary.StructuralTrend}");

        Console.WriteLine(
            $"BOS/CHoCH        : {(primary.BullishBOS || primary.BearishBOS ? "BOS" : primary.BullishCHoCH || primary.BearishCHoCH ? "CHoCH" : "NONE")}");

        Console.WriteLine(
            $"Breakout         : {(primary.ResistanceBreakout ? "RESISTANCE BREAK" : primary.SupportBreakdown ? "SUPPORT BREAK" : "NONE")}");

        Console.WriteLine(
            $"Retest           : {primary.RetestState}");

        Console.WriteLine(
            $"Candle Quality   : {primary.BreakoutCandleQuality}");

        Console.WriteLine(
            $"Market Regime    : {primary.MarketRegime}");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("CONFIDENCE BREAKDOWN");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Trend           : {primary.TrendScore:00}/20");

        Console.WriteLine(
            $"SMA/EMA         : {primary.SmaEmaScore:00}/15");

        Console.WriteLine(
            $"RSI             : {primary.RsiScore:00}/15");

        Console.WriteLine(
            $"MACD            : {primary.MacdScore:00}/15");

        Console.WriteLine(
            $"Volume          : {primary.VolumeScore:00}/10");

        Console.WriteLine(
            $"Support/Res.    : {primary.SupportResistanceScore:00}/10");

        Console.WriteLine(
            $"Multi-TF        : {primary.MultiTimeframeScore:00}/15");

        Console.WriteLine(
            $"TOTAL           : {primary.Confidence:F0}/100");

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("ANALYSIS REASONS");
        Console.WriteLine("------------------------------------------");

        if (primary.Reasons.Count == 0)
        {
            Console.WriteLine(
                "No enabled indicator provided independent confirmation.");
        }
        else
        {
            foreach (string reason in primary.Reasons)
            {
                Console.WriteLine(
                    "• " + reason);
            }
        }

        Console.WriteLine();
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("TRADE SETUP — PAPER / ANALYSIS ONLY");
        Console.WriteLine("------------------------------------------");

        PaperSetup? setup =
            BuildPaperSetup(
                symbol,
                primary,
                indicators);

        if (setup == null)
        {
            Console.WriteLine(
                "NO VALID SETUP");

            Console.WriteLine(
                "Insufficient confirmation, disabled ATR, or invalid risk.");
        }
        else
        {
            Console.WriteLine(
                $"Direction       : {setup.Direction}");

            Console.WriteLine(
                $"Entry Reference : {FormatPrice(setup.Entry)}");

            Console.WriteLine(
                $"Stop Loss       : {FormatPrice(setup.StopLoss)}");

            Console.WriteLine(
                $"Target 1        : {FormatPrice(setup.Target1)}");

            Console.WriteLine(
                $"Target 2        : {FormatPrice(setup.Target2)}");

            Console.WriteLine(
                $"Risk/Reward T1  : {setup.RiskRewardTarget1:F2}");

            Console.WriteLine(
                $"Risk/Reward T2  : {setup.RiskRewardTarget2:F2}");

            Console.WriteLine(
                $"Confidence      : {setup.Confidence:F0}/100");

            Console.WriteLine(
                "Execution       : PAPER ONLY");

            Console.WriteLine(
                "Real Order      : NONE");

            Console.WriteLine(
                "P/L basis       : 1 price-unit / no position quantity");
        }

        Console.WriteLine();
        PrintLastCandles(primary);

        Console.WriteLine();
        PrintScanHistory(scanHistory);

        Console.WriteLine();
        PrintPaperJournal(paperJournal);
        
        paperAccount.Print();
        Console.WriteLine();
        
        Console.WriteLine();
        PrintPerformance(paperHistory);

        Console.WriteLine();
        PrintV7PerformanceReport(paperHistory, paperAccount);

        Console.WriteLine();
        Console.WriteLine("==========================================");
        Console.WriteLine("V7 PAPER ANALYSIS COMPLETE");
        Console.WriteLine("NO AUTOMATIC REAL-MONEY ORDER IS PLACED");
        Console.WriteLine("==========================================");
    }

    private static async Task<List<Candle>> GetLiveCandlesAsync(
        HttpClient http,
        string symbol,
        string interval,
        int limit,
        ApiStats stats)
    {
        string url =
            "https://fapi.binance.com/fapi/v1/klines" +
            $"?symbol={Uri.EscapeDataString(symbol)}" +
            $"&interval={Uri.EscapeDataString(interval)}" +
            $"&limit={limit}";

        Exception? lastException = null;

        for (int attempt = 1;
             attempt <= MaxRetryAttempts;
             attempt++)
        {
            stats.TotalRequests++;

            try
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url);

                using HttpResponseMessage response =
                    await http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead);

                string content =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    stats.ApiErrors++;

                    string detail =
                        string.IsNullOrWhiteSpace(content)
                            ? response.ReasonPhrase
                              ?? "HTTP error."
                            : content;

                    bool retryable =
                        response.StatusCode ==
                            HttpStatusCode.TooManyRequests
                        || (int)response.StatusCode >= 500;

                    if (!retryable ||
                        attempt == MaxRetryAttempts)
                    {
                        throw new HttpRequestException(
                            $"HTTP {(int)response.StatusCode}: {detail}");
                    }

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(
                            600 * attempt));

                    continue;
                }

                using JsonDocument document =
                    JsonDocument.Parse(content);

                if (document.RootElement.ValueKind !=
                    JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        "Unexpected Binance Futures response format.");
                }

                var result =
                    new List<Candle>();

                foreach (JsonElement row in
                         document.RootElement.EnumerateArray())
                {
                    if (row.ValueKind !=
                        JsonValueKind.Array)
                    {
                        continue;
                    }

                    if (row.GetArrayLength() < 6)
                    {
                        continue;
                    }

                    if (!TryGetInt64(
                            row[0],
                            out long openTime))
                    {
                        continue;
                    }

                    if (!TryParseDecimal(
                            row[1],
                            out decimal open) ||
                        !TryParseDecimal(
                            row[2],
                            out decimal high) ||
                        !TryParseDecimal(
                            row[3],
                            out decimal low) ||
                        !TryParseDecimal(
                            row[4],
                            out decimal close) ||
                        !TryParseDecimal(
                            row[5],
                            out decimal volume))
                    {
                        continue;
                    }

                    if (!ValidateCandle(
                            openTime,
                            open,
                            high,
                            low,
                            close,
                            volume))
                    {
                        continue;
                    }

                    DateTime timestamp;

                    try
                    {
                        timestamp =
                            DateTimeOffset
                                .FromUnixTimeMilliseconds(
                                    openTime)
                                .UtcDateTime;
                    }
                    catch
                    {
                        continue;
                    }

                    result.Add(
                        new Candle(
                            timestamp,
                            open,
                            high,
                            low,
                            close,
                            volume));
                }

                List<Candle> cleaned =
                    result
                        .GroupBy(
                            c => c.Timestamp)
                        .Select(
                            g => g.First())
                        .OrderBy(
                            c => c.Timestamp)
                        .ToList();

                if (cleaned.Count == 0)
                {
                    throw new InvalidDataException(
                        "Binance returned no valid candles.");
                }

                // Binance kline payloads include the currently forming candle.
                // Signal generation must use only candles whose interval has
                // fully elapsed. Keep the latest candle only when its close
                // time is already in the past.
                while (cleaned.Count > 0 &&
                       !IsCandleClosed(
                           cleaned[^1].Timestamp,
                           interval,
                           DateTime.UtcNow))
                {
                    cleaned.RemoveAt(cleaned.Count - 1);
                }

                if (cleaned.Count == 0)
                {
                    throw new InvalidDataException(
                        "No fully closed candles are available yet.");
                }

                return cleaned;
            }
            catch (TaskCanceledException ex)
            {
                lastException = ex;

                stats.ApiTimeouts++;

                if (attempt == MaxRetryAttempts)
                    throw;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(
                        500 * attempt));
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;

                if (attempt == MaxRetryAttempts)
                    throw;

                await Task.Delay(
                    TimeSpan.FromMilliseconds(
                        600 * attempt));
            }
        }

        throw new HttpRequestException(
            lastException?.Message
            ?? "Market data request failed.");
    }

    private static bool IsCandleClosed(
        DateTime openTimestampUtc,
        string interval,
        DateTime nowUtc)
    {
        TimeSpan duration = interval.ToLowerInvariant() switch
        {
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "4h" => TimeSpan.FromHours(4),
            "1d" => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero
        };

        if (duration <= TimeSpan.Zero)
            return false;

        return openTimestampUtc + duration <= nowUtc;
    }

    private static bool ValidateCandle(
        long openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        if (openTime <= 0)
            return false;

        if (open <= 0 ||
            high <= 0 ||
            low <= 0 ||
            close <= 0)
        {
            return false;
        }

        if (volume < 0)
            return false;

        if (high < low)
            return false;

        if (high < open ||
            high < close)
        {
            return false;
        }

        if (low > open ||
            low > close)
        {
            return false;
        }

        DateTimeOffset timestamp;

        try
        {
            timestamp =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        openTime);
        }
        catch
        {
            return false;
        }

        if (timestamp <= DateTimeOffset.UnixEpoch)
            return false;

        if (timestamp >
            DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetInt64(
        JsonElement element,
        out long value)
    {
        value = 0;

        if (element.ValueKind ==
            JsonValueKind.Number)
        {
            return element.TryGetInt64(
                out value);
        }

        if (element.ValueKind ==
            JsonValueKind.String)
        {
            return long.TryParse(
                element.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static bool TryParseDecimal(
        JsonElement element,
        out decimal value)
    {
        value = 0;

        if (element.ValueKind ==
            JsonValueKind.Number)
        {
            return element.TryGetDecimal(
                out value);
        }

        if (element.ValueKind !=
            JsonValueKind.String)
        {
            return false;
        }

        return decimal.TryParse(
            element.GetString(),
            NumberStyles.Number |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static TimeframeAnalysis AnalyzeTimeframe(
    List<Candle> candles,
    string timeframe,
    IndicatorSettings settings)
{
    var closes =
        candles
            .Select(c => c.Close)
            .ToList();

    decimal? sma =
        settings.SmaEnabled
            ? CalculateSma(closes, 20)
            : null;

    decimal? ema =
        settings.EmaEnabled
            ? CalculateEma(closes, 20)
            : null;

    decimal? rsi =
        settings.RsiEnabled
            ? CalculateRsi(closes, 14)
            : null;

    (
        decimal? Macd,
        decimal? Signal,
        decimal? Histogram) macd =
            settings.MacdEnabled
                ? CalculateMacd(
                    closes,
                    12,
                    26,
                    9)
                : (null, null, null);

    decimal? atr =
        settings.AtrEnabled
            ? CalculateAtr(
                candles,
                14)
            : null;

    var recent =
        candles
            .TakeLast(20)
            .ToList();

    decimal support =
        recent.Count > 0
            ? recent.Min(c => c.Low)
            : 0m;

    decimal resistance =
        recent.Count > 0
            ? recent.Max(c => c.High)
            : 0m;

    decimal averageVolume =
        recent.Count > 0
            ? recent.Average(c => c.Volume)
            : 0m;

    Candle current =
        candles[^1];

    // =========================================================
    // MARKET STRUCTURE
    // =========================================================

    const int swingStrength = 2;

    decimal? lastSwingHigh = null;
    decimal? previousSwingHigh = null;

    decimal? lastSwingLow = null;
    decimal? previousSwingLow = null;

    // We deliberately exclude the latest candle from
    // confirmed swing detection. This prevents the current
    // unfinished candle from immediately becoming a swing.
    int lastConfirmedIndex =
        candles.Count - 2;

    if (lastConfirmedIndex >= swingStrength * 2)
    {
        var swingHighs =
            new List<(int Index, decimal Price)>();

        var swingLows =
            new List<(int Index, decimal Price)>();

        for (
            int i = swingStrength;
            i <= lastConfirmedIndex - swingStrength;
            i++)
        {
            bool isSwingHigh = true;

            bool isSwingLow = true;

            decimal high =
                candles[i].High;

            decimal low =
                candles[i].Low;

            for (
                int j = 1;
                j <= swingStrength;
                j++)
            {
                if (high <= candles[i - j].High ||
                    high <= candles[i + j].High)
                {
                    isSwingHigh = false;
                }

                if (low >= candles[i - j].Low ||
                    low >= candles[i + j].Low)
                {
                    isSwingLow = false;
                }
            }

            if (isSwingHigh)
            {
                swingHighs.Add(
                    (i, high));
            }

            if (isSwingLow)
            {
                swingLows.Add(
                    (i, low));
            }
        }

        if (swingHighs.Count > 0)
        {
            var orderedHighs =
                swingHighs
                    .OrderByDescending(
                        x => x.Index)
                    .ToList();

            lastSwingHigh =
                orderedHighs[0].Price;

            if (orderedHighs.Count > 1)
            {
                previousSwingHigh =
                    orderedHighs[1].Price;
            }
        }

        if (swingLows.Count > 0)
        {
            var orderedLows =
                swingLows
                    .OrderByDescending(
                        x => x.Index)
                    .ToList();

            lastSwingLow =
                orderedLows[0].Price;

            if (orderedLows.Count > 1)
            {
                previousSwingLow =
                    orderedLows[1].Price;
            }
        }
    }

    bool higherHigh =
        lastSwingHigh.HasValue &&
        previousSwingHigh.HasValue &&
        lastSwingHigh.Value >
        previousSwingHigh.Value;

    bool lowerHigh =
        lastSwingHigh.HasValue &&
        previousSwingHigh.HasValue &&
        lastSwingHigh.Value <
        previousSwingHigh.Value;

    bool higherLow =
        lastSwingLow.HasValue &&
        previousSwingLow.HasValue &&
        lastSwingLow.Value >
        previousSwingLow.Value;

    bool lowerLow =
        lastSwingLow.HasValue &&
        previousSwingLow.HasValue &&
        lastSwingLow.Value <
        previousSwingLow.Value;

    // =========================================================
    // SUPPORT / RESISTANCE CONTEXT
    // =========================================================
    // Preserve the existing recent high/low S/R calculation while
    // adding confirmed swing-derived levels, zones, tests,
    // rejection and level-flip state.
    var supportCandidates =
        new List<decimal>
        {
            support
        };

    var resistanceCandidates =
        new List<decimal>
        {
            resistance
        };

    if (lastSwingLow.HasValue)
        supportCandidates.Add(lastSwingLow.Value);

    if (previousSwingLow.HasValue)
        supportCandidates.Add(previousSwingLow.Value);

    if (lastSwingHigh.HasValue)
        resistanceCandidates.Add(lastSwingHigh.Value);

    if (previousSwingHigh.HasValue)
        resistanceCandidates.Add(previousSwingHigh.Value);

    decimal levelTolerance =
        atr.HasValue && atr.Value > 0
            ? atr.Value * 0.25m
            : Math.Max((resistance - support) * 0.01m, 0.00000001m);

    List<decimal> supportLevels =
        BuildDistinctLevels(
            supportCandidates,
            levelTolerance);

    List<decimal> resistanceLevels =
        BuildDistinctLevels(
            resistanceCandidates,
            levelTolerance);

    decimal nearestSupport =
        supportLevels
            .Where(level => level <= current.Close + levelTolerance)
            .OrderByDescending(level => level)
            .DefaultIfEmpty(support)
            .First();

    decimal nearestResistance =
        resistanceLevels
            .Where(level => level >= current.Close - levelTolerance)
            .OrderBy(level => level)
            .DefaultIfEmpty(resistance)
            .First();

    decimal supportZoneLow =
        Math.Max(0m, nearestSupport - levelTolerance);

    decimal supportZoneHigh =
        nearestSupport + levelTolerance;

    decimal resistanceZoneLow =
        Math.Max(0m, nearestResistance - levelTolerance);

    decimal resistanceZoneHigh =
        nearestResistance + levelTolerance;

    bool supportTested =
        current.Low <= supportZoneHigh &&
        current.Close >= nearestSupport;

    bool resistanceTested =
        current.High >= resistanceZoneLow &&
        current.Close <= nearestResistance;

    decimal bodySize =
        Math.Abs(current.Close - current.Open);

    decimal upperWick =
        Math.Max(0m, current.High - Math.Max(current.Open, current.Close));

    decimal lowerWick =
        Math.Max(0m, Math.Min(current.Open, current.Close) - current.Low);

    bool supportRejection =
        supportTested &&
        current.Close > current.Open &&
        lowerWick > bodySize;

    bool resistanceRejection =
        resistanceTested &&
        current.Close < current.Open &&
        upperWick > bodySize;

    bool bullishLevelFlip =
        lastSwingHigh.HasValue &&
        current.Close > lastSwingHigh.Value &&
        current.Low <= lastSwingHigh.Value;

    bool bearishLevelFlip =
        lastSwingLow.HasValue &&
        current.Close < lastSwingLow.Value &&
        current.High >= lastSwingLow.Value;

    string levelFlip =
        bullishLevelFlip
            ? "RESISTANCE -> SUPPORT"
            : bearishLevelFlip
                ? "SUPPORT -> RESISTANCE"
                : "NONE";

    // =========================================================
    // STRUCTURE BREAK / BREAKOUT / CANDLE QUALITY
    // =========================================================
    decimal totalRange =
        Math.Max(0m, current.High - current.Low);

    decimal bodyRangeRatio =
        totalRange > 0m
            ? bodySize / totalRange
            : 0m;

    bool bullishCandle =
        current.Close > current.Open;

    bool bearishCandle =
        current.Close < current.Open;

    bool strongCandle =
        totalRange > 0m &&
        bodyRangeRatio >= 0.60m;

    bool weakCandle =
        totalRange <= 0m ||
        bodyRangeRatio < 0.30m;

    bool rejectionCandle =
        (bullishCandle && lowerWick > bodySize) ||
        (bearishCandle && upperWick > bodySize);
    string marketStructure =
        "UNDEFINED";

    string structuralTrend =
        "UNDEFINED";

    if (higherHigh && higherLow)
    {
        marketStructure =
            "HH + HL";

        structuralTrend =
            "BULLISH";
    }
    else if (lowerHigh && lowerLow)
    {
        marketStructure =
            "LH + LL";

        structuralTrend =
            "BEARISH";
    }
    else if (higherHigh)
    {
        marketStructure =
            "HH";

        structuralTrend =
            "BULLISH";
    }
    else if (higherLow)
    {
        marketStructure =
            "HL";

        structuralTrend =
            "BULLISH";
    }
    else if (lowerHigh)
    {
        marketStructure =
            "LH";

        structuralTrend =
            "BEARISH";
    }
    else if (lowerLow)
    {
        marketStructure =
            "LL";

        structuralTrend =
            "BEARISH";
    }

    bool continuationCandle =
        strongCandle &&
        ((bullishCandle && structuralTrend == "BULLISH") ||
         (bearishCandle && structuralTrend == "BEARISH"));

    bool bullishCloseBreak =
        lastSwingHigh.HasValue &&
        current.Close > lastSwingHigh.Value;

    bool bearishCloseBreak =
        lastSwingLow.HasValue &&
        current.Close < lastSwingLow.Value;

    bool bullishWickBreakOnly =
        lastSwingHigh.HasValue &&
        current.High > lastSwingHigh.Value &&
        current.Close <= lastSwingHigh.Value;

    bool bearishWickBreakOnly =
        lastSwingLow.HasValue &&
        current.Low < lastSwingLow.Value &&
        current.Close >= lastSwingLow.Value;

    bool bullishBos =
        bullishCloseBreak &&
        structuralTrend == "BULLISH";

    bool bearishBos =
        bearishCloseBreak &&
        structuralTrend == "BEARISH";

    bool bullishChoch =
        bullishCloseBreak &&
        structuralTrend == "BEARISH";

    bool bearishChoch =
        bearishCloseBreak &&
        structuralTrend == "BULLISH";

    bool resistanceBreakout =
        bullishCloseBreak &&
        !bullishWickBreakOnly;

    bool supportBreakdown =
        bearishCloseBreak &&
        !bearishWickBreakOnly;

    bool breakoutVolumeConfirmed =
        !settings.VolumeEnabled ||
        (averageVolume > 0m &&
         current.Volume >= averageVolume);

    string breakoutStrength =
        (resistanceBreakout || supportBreakdown)
            ? (strongCandle && breakoutVolumeConfirmed
                ? "STRONG"
                : "WEAK")
            : "NONE";

    string breakoutCandleQuality =
        (resistanceBreakout || supportBreakdown)
            ? (strongCandle && breakoutVolumeConfirmed
                ? "VALID"
                : "WEAK / REJECT")
            : "NONE";

    bool weakBreakoutRejected =
        (resistanceBreakout || supportBreakdown) &&
        (!strongCandle || !breakoutVolumeConfirmed);

    // Breakout/retest state is evaluated only from candles that are already
    // closed. The current candle is therefore the confirmation candle, not
    // an unfinished source of historical structure.
    int lastBullishBreakoutIndex = -1;
    int lastBearishBreakoutIndex = -1;

    if (lastSwingHigh.HasValue)
    {
        for (int i = candles.Count - 2; i >= 1; i--)
        {
            if (candles[i].Close > lastSwingHigh.Value &&
                candles[i - 1].Close <= lastSwingHigh.Value)
            {
                lastBullishBreakoutIndex = i;
                break;
            }
        }
    }

    if (lastSwingLow.HasValue)
    {
        for (int i = candles.Count - 2; i >= 1; i--)
        {
            if (candles[i].Close < lastSwingLow.Value &&
                candles[i - 1].Close >= lastSwingLow.Value)
            {
                lastBearishBreakoutIndex = i;
                break;
            }
        }
    }

    bool bullishRetest =
        lastBullishBreakoutIndex >= 0 &&
        lastBullishBreakoutIndex < candles.Count - 1 &&
        current.Low <= lastSwingHigh.GetValueOrDefault() + levelTolerance &&
        current.Close > lastSwingHigh.GetValueOrDefault();

    bool bearishRetest =
        lastBearishBreakoutIndex >= 0 &&
        lastBearishBreakoutIndex < candles.Count - 1 &&
        current.High >= lastSwingLow.GetValueOrDefault() - levelTolerance &&
        current.Close < lastSwingLow.GetValueOrDefault();

    bool failedBullishRetest =
        lastBullishBreakoutIndex >= 0 &&
        current.Low < lastSwingHigh.GetValueOrDefault() - levelTolerance &&
        current.Close < lastSwingHigh.GetValueOrDefault();

    bool failedBearishRetest =
        lastBearishBreakoutIndex >= 0 &&
        current.High > lastSwingLow.GetValueOrDefault() + levelTolerance &&
        current.Close > lastSwingLow.GetValueOrDefault();

    string retestState =
        bullishRetest
            ? "BULLISH RETEST CONFIRMED"
            : bearishRetest
                ? "BEARISH RETEST CONFIRMED"
                : failedBullishRetest
                    ? "FAILED BULLISH RETEST"
                    : failedBearishRetest
                        ? "FAILED BEARISH RETEST"
                        : "NONE";

    // Use the nearest structure-aware levels for price location.
    support = nearestSupport;
    resistance = nearestResistance;


    // =========================================================
    // INVALIDATION / MARKET REGIME
    // =========================================================
    bool longStructureInvalidated =
        lastSwingLow.HasValue &&
        current.Close < lastSwingLow.Value;

    bool shortStructureInvalidated =
        lastSwingHigh.HasValue &&
        current.Close > lastSwingHigh.Value;

    bool breakoutFailure =
        bullishWickBreakOnly ||
        bearishWickBreakOnly ||
        failedBullishRetest ||
        failedBearishRetest;

    bool invalidation =
        longStructureInvalidated ||
        shortStructureInvalidated ||
        breakoutFailure;

    string marketRegime =
        structuralTrend == "BULLISH" || structuralTrend == "BEARISH"
            ? (bodyRangeRatio >= 0.45m ? "TRENDING" : "CHOPPY")
            : (resistance > support &&
               (resistance - support) > Math.Max(current.Close * 0.002m, levelTolerance * 2m)
                ? "RANGING"
                : "SIDEWAYS");

    // =========================================================
    // INDICATOR TREND
    // =========================================================

    string trend =
        "SIDEWAYS";

    MarketDirection direction =
        MarketDirection.Neutral;

    if (settings.SmaEnabled &&
        settings.EmaEnabled &&
        sma.HasValue &&
        ema.HasValue)
    {
        if (current.Close > sma.Value &&
            current.Close > ema.Value)
        {
            trend = "BULLISH";
            direction = MarketDirection.Bullish;
        }
        else if (current.Close < sma.Value &&
                 current.Close < ema.Value)
        {
            trend = "BEARISH";
            direction = MarketDirection.Bearish;
        }
    }
    else if (settings.SmaEnabled &&
             sma.HasValue)
    {
        if (current.Close > sma.Value)
        {
            trend = "BULLISH";
            direction = MarketDirection.Bullish;
        }
        else if (current.Close < sma.Value)
        {
            trend = "BEARISH";
            direction = MarketDirection.Bearish;
        }
    }
    else if (settings.EmaEnabled &&
             ema.HasValue)
    {
        if (current.Close > ema.Value)
        {
            trend = "BULLISH";
            direction = MarketDirection.Bullish;
        }
        else if (current.Close < ema.Value)
        {
            trend = "BEARISH";
            direction = MarketDirection.Bearish;
        }
    }

    // =========================================================
    // VOLUME
    // =========================================================

    string volumeStatus;

    if (!settings.VolumeEnabled)
    {
        volumeStatus =
            "DISABLED";
    }
    else
    {
        volumeStatus =
            current.Volume >= averageVolume
                ? "ABOVE AVERAGE"
                : "BELOW AVERAGE";
    }

    // =========================================================
    // PRICE LOCATION
    // =========================================================

    string pricePosition =
        CalculatePricePosition(
            current.Close,
            support,
            resistance);

    // =========================================================
    // REASONS
    // =========================================================

    var reasons =
        new List<string>();

    // Indicator trend reason
    if (settings.SmaEnabled &&
        settings.EmaEnabled)
    {
        if (trend == "BULLISH")
        {
            reasons.Add(
                "Price is above both enabled SMA20 and EMA20.");
        }
        else if (trend == "BEARISH")
        {
            reasons.Add(
                "Price is below both enabled SMA20 and EMA20.");
        }
        else
        {
            reasons.Add(
                "Enabled SMA20/EMA20 alignment is not directional.");
        }
    }
    else if (settings.SmaEnabled)
    {
        if (trend == "BULLISH")
        {
            reasons.Add(
                "Price is above enabled SMA20.");
        }
        else if (trend == "BEARISH")
        {
            reasons.Add(
                "Price is below enabled SMA20.");
        }
        else
        {
            reasons.Add(
                "Enabled SMA20 is not directional.");
        }
    }
    else if (settings.EmaEnabled)
    {
        if (trend == "BULLISH")
        {
            reasons.Add(
                "Price is above enabled EMA20.");
        }
        else if (trend == "BEARISH")
        {
            reasons.Add(
                "Price is below enabled EMA20.");
        }
        else
        {
            reasons.Add(
                "Enabled EMA20 is not directional.");
        }
    }
    else
    {
        reasons.Add(
            "SMA and EMA are disabled; trend alignment is unavailable.");
    }

    // Market structure reasons
    if (marketStructure != "UNDEFINED")
    {
        reasons.Add(
            $"Market structure: {marketStructure}.");

        reasons.Add(
            $"Structural trend: {structuralTrend}.");
    }
    else
    {
        reasons.Add(
            "Confirmed market structure is not yet available.");
    }

    if (lastSwingHigh.HasValue)
    {
        reasons.Add(
            $"Last confirmed swing high: {lastSwingHigh.Value}.");
    }

    if (lastSwingLow.HasValue)
    {
        reasons.Add(
            $"Last confirmed swing low: {lastSwingLow.Value}.");
    }

    // RSI
    if (settings.RsiEnabled &&
        rsi.HasValue)
    {
        if (rsi.Value >= 55 &&
            rsi.Value <= 70)
        {
            reasons.Add(
                $"RSI is in a bullish confirmation zone: {rsi.Value:F2}.");
        }
        else if (rsi.Value >= 30 &&
                 rsi.Value <= 45)
        {
            reasons.Add(
                $"RSI is in a bearish confirmation zone: {rsi.Value:F2}.");
        }
        else if (rsi.Value > 70)
        {
            reasons.Add(
                $"RSI is overbought: {rsi.Value:F2}.");
        }
        else if (rsi.Value < 30)
        {
            reasons.Add(
                $"RSI is oversold: {rsi.Value:F2}.");
        }
        else
        {
            reasons.Add(
                $"RSI is neutral: {rsi.Value:F2}.");
        }
    }

    // MACD
    if (settings.MacdEnabled &&
        macd.Histogram.HasValue)
    {
        if (macd.Histogram.Value > 0)
        {
            reasons.Add(
                "MACD histogram is positive.");
        }
        else if (macd.Histogram.Value < 0)
        {
            reasons.Add(
                "MACD histogram is negative.");
        }
        else
        {
            reasons.Add(
                "MACD histogram is neutral.");
        }
    }

    // Volume reason
    if (settings.VolumeEnabled)
    {
        if (current.Volume >= averageVolume)
        {
            reasons.Add(
                "Current volume is at or above its recent average.");
        }
        else
        {
            reasons.Add(
                "Current volume is below its recent average.");
        }
    }

    // Price location reason
    if (pricePosition == "NEAR RESISTANCE")
    {
        reasons.Add(
            "Price is close to the recent resistance area.");
    }
    else if (pricePosition == "NEAR SUPPORT")
    {
        reasons.Add(
            "Price is close to the recent support area.");
    }

    // =========================================================
    // MOMENTUM CONFIRMATION
    // =========================================================

    bool bullishMomentumConfirmed =
        true;

    bool bearishMomentumConfirmed =
        true;

    bool momentumIndicatorUsed =
        false;

    if (settings.RsiEnabled)
    {
        momentumIndicatorUsed =
            true;

        bullishMomentumConfirmed &=
            rsi.HasValue &&
            rsi.Value >= 55 &&
            rsi.Value <= 70;

        bearishMomentumConfirmed &=
            rsi.HasValue &&
            rsi.Value >= 30 &&
            rsi.Value <= 45;
    }

    if (settings.MacdEnabled)
    {
        momentumIndicatorUsed =
            true;

        bullishMomentumConfirmed &=
            macd.Histogram.HasValue &&
            macd.Histogram.Value > 0;

        bearishMomentumConfirmed &=
            macd.Histogram.HasValue &&
            macd.Histogram.Value < 0;
    }

    // =========================================================
    // SIGNAL
    // =========================================================

    bool volumeConfirmedForDirection =
        !settings.VolumeEnabled ||
        (averageVolume > 0m && current.Volume >= averageVolume);

    bool bullishStructureContext =
        structuralTrend == "BULLISH" &&
        !longStructureInvalidated &&
        (bullishBos || bullishChoch || supportRejection || bullishRetest || levelFlip == "RESISTANCE -> SUPPORT");

    bool bearishStructureContext =
        structuralTrend == "BEARISH" &&
        !shortStructureInvalidated &&
        (bearishBos || bearishChoch || resistanceRejection || bearishRetest || levelFlip == "SUPPORT -> RESISTANCE");

    bool momentumConfirmed =
        momentumIndicatorUsed &&
        (bullishMomentumConfirmed || bearishMomentumConfirmed);

    bool bullishStrongSetup =
        direction == MarketDirection.Bullish &&
        bullishStructureContext &&
        momentumIndicatorUsed &&
        bullishMomentumConfirmed &&
        settings.VolumeEnabled &&
        volumeConfirmedForDirection &&
        !weakBreakoutRejected &&
        !invalidation;

    bool bearishStrongSetup =
        direction == MarketDirection.Bearish &&
        bearishStructureContext &&
        momentumIndicatorUsed &&
        bearishMomentumConfirmed &&
        settings.VolumeEnabled &&
        volumeConfirmedForDirection &&
        !weakBreakoutRejected &&
        !invalidation;

    string signal =
        bullishStrongSetup
            ? "BULLISH WATCH"
            : bearishStrongSetup
                ? "BEARISH WATCH"
                : "WAIT";

    if (strongCandle)
        reasons.Add("Candle body/range quality is strong.");
    else if (weakCandle)
        reasons.Add("Candle body/range quality is weak.");

    if (rejectionCandle)
        reasons.Add("Rejection candle detected.");

    if (continuationCandle)
        reasons.Add("Continuation candle agrees with structural trend.");

    if (bullishBos || bearishBos)
        reasons.Add("Break of Structure confirmed.");

    if (bullishChoch || bearishChoch)
        reasons.Add("Change of Character detected.");

    if (resistanceBreakout || supportBreakdown)
        reasons.Add($"Breakout strength: {breakoutStrength}.");

    if (weakBreakoutRejected)
        reasons.Add("Weak breakout rejected: candle/volume confirmation failed.");

    if (retestState != "NONE")
        reasons.Add($"Retest state: {retestState}.");

    reasons.Add($"Market regime: {marketRegime}.");

    if (invalidation)
        reasons.Add("Setup invalidation condition is active.");

    // =========================================================
    // STRUCTURE-AWARE SL / TARGETS
    // =========================================================
    decimal structureStopLoss = 0m;
    decimal structureTarget1 = 0m;
    decimal structureTarget2 = 0m;

    decimal safetyBuffer =
        atr.HasValue && atr.Value > 0m
            ? atr.Value * 0.10m
            : Math.Max(totalRange * 0.10m, current.Close * 0.0005m);

    if (direction == MarketDirection.Bullish)
    {
        if (lastSwingLow.HasValue && lastSwingLow.Value < current.Close)
            structureStopLoss = lastSwingLow.Value - safetyBuffer;

        decimal[] upsideLevels =
            resistanceLevels
                .Where(level => level > current.Close + levelTolerance)
                .OrderBy(level => level)
                .ToArray();

        if (upsideLevels.Length > 0)
            structureTarget1 = upsideLevels[0];

        if (upsideLevels.Length > 1)
            structureTarget2 = upsideLevels[1];
    }
    else if (direction == MarketDirection.Bearish)
    {
        if (lastSwingHigh.HasValue && lastSwingHigh.Value > current.Close)
            structureStopLoss = lastSwingHigh.Value + safetyBuffer;

        decimal[] downsideLevels =
            supportLevels
                .Where(level => level < current.Close - levelTolerance)
                .OrderByDescending(level => level)
                .ToArray();

        if (downsideLevels.Length > 0)
            structureTarget1 = downsideLevels[0];

        if (downsideLevels.Length > 1)
            structureTarget2 = downsideLevels[1];
    }

    // =========================================================
    // RETURN
    // =========================================================

    return new TimeframeAnalysis
    {
        Timeframe = timeframe,

        Candles = candles,

        Timestamp =
            current.Timestamp,

        CurrentPrice =
            current.Close,

        Open =
            current.Open,

        High =
            current.High,

        Low =
            current.Low,

        Close =
            current.Close,

        Volume =
            current.Volume,

        Trend =
            trend,

        Direction =
            direction,

        Sma =
            sma,

        Ema =
            ema,

        Rsi =
            rsi,

        Macd =
            macd.Macd,

        MacdSignal =
            macd.Signal,

        MacdHistogram =
            macd.Histogram,

        Atr =
            atr,

        Support =
            support,

        Resistance =
            resistance,

        AverageVolume =
            averageVolume,

        VolumeStatus =
            volumeStatus,

        PricePosition =
            pricePosition,

        SupportLevels =
            supportLevels,

        ResistanceLevels =
            resistanceLevels,

        SupportZoneLow =
            supportZoneLow,

        SupportZoneHigh =
            supportZoneHigh,

        ResistanceZoneLow =
            resistanceZoneLow,

        ResistanceZoneHigh =
            resistanceZoneHigh,

        SupportTested =
            supportTested,

        ResistanceTested =
            resistanceTested,

        SupportRejection =
            supportRejection,

        ResistanceRejection =
            resistanceRejection,

        LevelFlip =
            levelFlip,

        LastSwingHigh =
            lastSwingHigh,

        LastSwingLow =
            lastSwingLow,

        PreviousSwingHigh =
            previousSwingHigh,

        PreviousSwingLow =
            previousSwingLow,

        MarketStructure =
            marketStructure,

        StructuralTrend =
            structuralTrend,

        HigherHigh =
            higherHigh,

        HigherLow =
            higherLow,

        LowerHigh =
            lowerHigh,

        LowerLow =
            lowerLow,

        Signal =
            signal,

        BodySize =
            bodySize,

        TotalRange =
            totalRange,

        UpperWick =
            upperWick,

        LowerWick =
            lowerWick,

        BodyRangeRatio =
            bodyRangeRatio,

        BullishCandle =
            bullishCandle,

        BearishCandle =
            bearishCandle,

        StrongCandle =
            strongCandle,

        WeakCandle =
            weakCandle,

        RejectionCandle =
            rejectionCandle,

        ContinuationCandle =
            continuationCandle,

        BreakoutCandleQuality =
            breakoutCandleQuality,

        BullishBOS =
            bullishBos,

        BearishBOS =
            bearishBos,

        BullishCHoCH =
            bullishChoch,

        BearishCHoCH =
            bearishChoch,

        ResistanceBreakout =
            resistanceBreakout,

        SupportBreakdown =
            supportBreakdown,

        CloseConfirmedBreakout =
            resistanceBreakout || supportBreakdown,

        WickOnlyBreakout =
            bullishWickBreakOnly || bearishWickBreakOnly,

        BreakoutStrength =
            breakoutStrength,

        BreakoutVolumeConfirmed =
            breakoutVolumeConfirmed,

        WeakBreakoutRejected =
            weakBreakoutRejected,

        RetestState =
            retestState,

        RetestConfirmed =
            bullishRetest || bearishRetest,

        RetestFailed =
            failedBullishRetest || failedBearishRetest,

        Invalidation =
            invalidation,

        LongInvalidation =
            longStructureInvalidated,

        ShortInvalidation =
            shortStructureInvalidated,

        MarketRegime =
            marketRegime,

        StrongSetup =
            bullishStrongSetup || bearishStrongSetup,

        MomentumConfirmed =
            momentumConfirmed,

        VolumeConfirmed =
            volumeConfirmedForDirection,

        StructureStopLoss =
            structureStopLoss,

        StructureTarget1 =
            structureTarget1,

        StructureTarget2 =
            structureTarget2,

        Reasons =
            reasons
    };
}

    private static List<decimal> BuildDistinctLevels(
        IEnumerable<decimal> candidates,
        decimal tolerance)
    {
        var result =
            new List<decimal>();

        decimal safeTolerance =
            tolerance > 0
                ? tolerance
                : 0.00000001m;

        foreach (decimal level in
                 candidates
                    .Where(value => value > 0)
                    .OrderBy(value => value))
        {
            if (result.Count == 0 ||
                Math.Abs(result[^1] - level) > safeTolerance)
            {
                result.Add(level);
            }
            else
            {
                result[^1] =
                    (result[^1] + level) / 2m;
            }
        }

        return result;
    }

    private static string CalculatePricePosition(
        decimal price,
        decimal support,
        decimal resistance)
    {
        if (resistance <= support)
            return "MID RANGE";

        decimal range =
            resistance - support;

        decimal position =
            (price - support) / range;

        if (position >= 0.90m)
            return "NEAR RESISTANCE";

        if (position <= 0.10m)
            return "NEAR SUPPORT";

        if (position >= 0.65m)
            return "UPPER RANGE";

        if (position <= 0.35m)
            return "LOWER RANGE";

        return "MID RANGE";
    }

    private static void ApplyMultiTimeframeMetrics(
        List<TimeframeAnalysis> analyses)
    {
        foreach (TimeframeAnalysis analysis in analyses)
        {
            int agreement =
                analysis.Direction ==
                    MarketDirection.Neutral
                    ? 0
                    : analyses.Count(
                        a =>
                            a.Direction ==
                            analysis.Direction);

            analysis.MultiTimeframeAgreement =
                agreement;

            analysis.MultiTimeframeScore =
                CalculateMultiTimeframeScore(
                    agreement,
                    analyses.Count,
                    analysis.Direction);

            string structuralDirection =
                analysis.StructuralTrend;

            analysis.StructuralAgreementCount =
                !string.IsNullOrWhiteSpace(structuralDirection) &&
                structuralDirection != "UNDEFINED"
                    ? analyses.Count(
                        a => a.StructuralTrend == structuralDirection)
                    : 0;

            bool hasBullish =
                analyses.Any(
                    a => a.Direction == MarketDirection.Bullish);

            bool hasBearish =
                analyses.Any(
                    a => a.Direction == MarketDirection.Bearish);

            analysis.MultiTimeframeConflict =
                hasBullish && hasBearish;

            var higherTimeframes =
                analyses
                    .Where(
                        a =>
                            a.Timeframe.Equals("4h", StringComparison.OrdinalIgnoreCase) ||
                            a.Timeframe.Equals("1d", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (higherTimeframes.Count > 0)
            {
                analysis.HigherTimeframeSupported =
                    analysis.Direction != MarketDirection.Neutral &&
                    higherTimeframes.All(
                        a => a.Direction == analysis.Direction);

                analysis.HigherTimeframeStructure =
                    higherTimeframes.All(
                        a => a.StructuralTrend == structuralDirection)
                        ? structuralDirection
                        : higherTimeframes
                            .Select(a => a.StructuralTrend)
                            .FirstOrDefault(
                                value => value != "UNDEFINED")
                          ?? "UNDEFINED";
            }
        }
    }

    private static int CalculateMultiTimeframeScore(
        int agreement,
        int total,
        MarketDirection direction)
    {
        if (direction ==
                MarketDirection.Neutral ||
            total <= 0)
        {
            return 0;
        }

        decimal ratio =
            agreement / (decimal)total;

        if (ratio >= 0.75m)
            return 15;

        if (ratio >= 0.50m)
            return 10;

        if (ratio >= 0.25m)
            return 5;

        return 0;
    }

    private static decimal CalculateConfidence(
        TimeframeAnalysis analysis,
        IndicatorSettings settings)
    {
        int trendScore = 0;
        int smaEmaScore = 0;
        int rsiScore = 0;
        int macdScore = 0;
        int volumeScore = 0;
        int supportResistanceScore = 0;

        if (analysis.Direction !=
            MarketDirection.Neutral)
        {
            trendScore = 20;
        }

        if (settings.SmaEnabled ||
            settings.EmaEnabled)
        {
            bool bullishAlignment = false;
            bool bearishAlignment = false;

            if (settings.SmaEnabled &&
                settings.EmaEnabled &&
                analysis.Sma.HasValue &&
                analysis.Ema.HasValue)
            {
                bullishAlignment =
                    analysis.Close > analysis.Sma.Value &&
                    analysis.Close > analysis.Ema.Value;

                bearishAlignment =
                    analysis.Close < analysis.Sma.Value &&
                    analysis.Close < analysis.Ema.Value;
            }
            else if (settings.SmaEnabled &&
                     analysis.Sma.HasValue)
            {
                bullishAlignment =
                    analysis.Close > analysis.Sma.Value;

                bearishAlignment =
                    analysis.Close < analysis.Sma.Value;
            }
            else if (settings.EmaEnabled &&
                     analysis.Ema.HasValue)
            {
                bullishAlignment =
                    analysis.Close > analysis.Ema.Value;

                bearishAlignment =
                    analysis.Close < analysis.Ema.Value;
            }

            if ((analysis.Direction ==
                    MarketDirection.Bullish &&
                 bullishAlignment) ||
                (analysis.Direction ==
                    MarketDirection.Bearish &&
                 bearishAlignment))
            {
                smaEmaScore = 15;
            }
        }

        if (settings.RsiEnabled &&
            analysis.Rsi.HasValue)
        {
            if (analysis.Direction ==
                    MarketDirection.Bullish &&
                analysis.Rsi.Value >= 55 &&
                analysis.Rsi.Value <= 70)
            {
                rsiScore = 15;
            }
            else if (
                analysis.Direction ==
                    MarketDirection.Bearish &&
                analysis.Rsi.Value >= 30 &&
                analysis.Rsi.Value <= 45)
            {
                rsiScore = 15;
            }
        }

        if (settings.MacdEnabled &&
            analysis.Macd.HasValue &&
            analysis.MacdSignal.HasValue &&
            analysis.MacdHistogram.HasValue)
        {
            if (analysis.Direction ==
                    MarketDirection.Bullish &&
                analysis.Macd >
                    analysis.MacdSignal &&
                analysis.MacdHistogram > 0)
            {
                macdScore = 15;
            }
            else if (
                analysis.Direction ==
                    MarketDirection.Bearish &&
                analysis.Macd <
                    analysis.MacdSignal &&
                analysis.MacdHistogram < 0)
            {
                macdScore = 15;
            }
        }

        if (settings.VolumeEnabled &&
            analysis.AverageVolume > 0 &&
            analysis.Volume >= analysis.AverageVolume)
        {
            if (analysis.Direction ==
                    MarketDirection.Bullish &&
                analysis.Close >= analysis.Open)
            {
                volumeScore = 10;
            }
            else if (
                analysis.Direction ==
                    MarketDirection.Bearish &&
                analysis.Close <= analysis.Open)
            {
                volumeScore = 10;
            }
            else
            {
                volumeScore = 5;
            }
        }

        if (analysis.Direction ==
            MarketDirection.Bullish)
        {
            if (analysis.PricePosition ==
                "NEAR SUPPORT")
            {
                supportResistanceScore = 10;
            }
            else if (
                analysis.PricePosition !=
                "NEAR RESISTANCE")
            {
                supportResistanceScore = 7;
            }
        }
        else if (analysis.Direction ==
                 MarketDirection.Bearish)
        {
            if (analysis.PricePosition ==
                "NEAR RESISTANCE")
            {
                supportResistanceScore = 10;
            }
            else if (
                analysis.PricePosition !=
                "NEAR SUPPORT")
            {
                supportResistanceScore = 7;
            }
        }

        analysis.TrendScore =
            trendScore;

        analysis.SmaEmaScore =
            smaEmaScore;

        analysis.RsiScore =
            rsiScore;

        analysis.MacdScore =
            macdScore;

        analysis.VolumeScore =
            volumeScore;

        analysis.SupportResistanceScore =
            supportResistanceScore;

        int total =
            trendScore +
            smaEmaScore +
            rsiScore +
            macdScore +
            volumeScore +
            supportResistanceScore +
            analysis.MultiTimeframeScore;

        return Math.Clamp(
            total,
            0,
            100);
    }

    private static string CalculateMultiTimeframeSignal(
        List<TimeframeAnalysis> analyses)
    {
        int bullish =
            analyses.Count(
                a => a.Direction ==
                     MarketDirection.Bullish);

        int bearish =
            analyses.Count(
                a => a.Direction ==
                     MarketDirection.Bearish);

        if (bullish >= 3 &&
            bullish > bearish)
        {
            return "BULLISH MULTI-TF WATCH";
        }

        if (bearish >= 3 &&
            bearish > bullish)
        {
            return "BEARISH MULTI-TF WATCH";
        }

        if (bullish > 0 &&
            bearish == 0)
        {
            return "BULLISH — LIMITED CONFIRMATION";
        }

        if (bearish > 0 &&
            bullish == 0)
        {
            return "BEARISH — LIMITED CONFIRMATION";
        }

        return "WAIT — MIXED / INSUFFICIENT CONFIRMATION";
    }

    private static decimal CalculateOverallConfidence(
        List<TimeframeAnalysis> analyses)
    {
        if (analyses.Count == 0)
            return 0;

        var directional =
            analyses
                .Where(
                    a => a.Direction !=
                         MarketDirection.Neutral)
                .ToList();

        if (directional.Count == 0)
            return 0;

        decimal average =
            directional.Average(
                a => a.Confidence);

        return Math.Clamp(
            average,
            0,
            100);
    }

    private static string GetDisplaySignal(
        TimeframeAnalysis primary)
    {
        if (primary.Direction ==
            MarketDirection.Bullish)
        {
            if (primary.Confidence >= 80 &&
                primary.MultiTimeframeAgreement >= 3)
            {
                return "STRONG BULLISH WATCH";
            }

            return "BULLISH WATCH";
        }

        if (primary.Direction ==
            MarketDirection.Bearish)
        {
            if (primary.Confidence >= 80 &&
                primary.MultiTimeframeAgreement >= 3)
            {
                return "STRONG BEARISH WATCH";
            }

            return "BEARISH WATCH";
        }

        return "WAIT";
    }

    private static bool IsDuplicatePrimaryAnalysis(
        string symbol,
        TimeframeAnalysis primary,
        List<ScanHistoryRecord> history)
    {
        return history.Any(
            h =>
                h.Coin.Equals(
                    symbol,
                    StringComparison.OrdinalIgnoreCase) &&
                h.PrimaryTimeframe.Equals(
                    primary.Timeframe,
                    StringComparison.OrdinalIgnoreCase) &&
                h.CandleTimestamp ==
                    primary.Timestamp);
    }

    private static PaperSetup? BuildPaperSetup(
        string symbol,
        TimeframeAnalysis primary,
        IndicatorSettings settings)
    {
        if (primary.Signal.Equals(
        "WAIT",
        StringComparison.OrdinalIgnoreCase))
{
    return null;
}
        if (primary.Direction ==
            MarketDirection.Neutral)
        {
            return null;
        }

        if (primary.Confidence < settings.StrongSetupMinimumConfidence)
        {
            return null;
        }

        if (primary.MultiTimeframeAgreement < settings.MinimumMultiTimeframeAgreement)
        {
            return null;
        }

        // Structural direction must agree with the entry direction.
        if (primary.Direction == MarketDirection.Bullish &&
            !primary.StructuralTrend.Equals(
                "BULLISH",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (primary.Direction == MarketDirection.Bearish &&
            !primary.StructuralTrend.Equals(
                "BEARISH",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Strong setup gate: all required structural/confirmation layers
        // must agree before a paper trade can be created.
        bool directionContext =
            primary.Direction == MarketDirection.Bullish
                ? primary.SupportRejection || primary.RetestConfirmed || primary.LevelFlip == "RESISTANCE -> SUPPORT"
                : primary.ResistanceRejection || primary.RetestConfirmed || primary.LevelFlip == "SUPPORT -> RESISTANCE";

        if (!directionContext ||
            !primary.StrongSetup ||
            !primary.MomentumConfirmed ||
            !primary.VolumeConfirmed ||
            primary.Invalidation ||
            primary.RetestFailed ||
            !primary.RetestConfirmed ||
            !primary.StrongCandle ||
            primary.MultiTimeframeConflict ||
            !primary.HigherTimeframeSupported ||
            primary.StructuralAgreementCount < settings.MinimumMultiTimeframeAgreement ||
            primary.MarketRegime == "CHOPPY" ||
            primary.MarketRegime == "SIDEWAYS")
        {
            return null;
        }

        // ATR disabled or unavailable means no ATR-based paper setup.
        if (!primary.Atr.HasValue ||
            primary.Atr.Value <= 0)
        {
            return null;
        }

        decimal entry =
            primary.CurrentPrice;

        decimal atr =
            primary.Atr.Value;

        decimal atrRiskDistance =
            atr * 0.5m;

        if (atrRiskDistance <= 0)
            return null;

        string direction;
        decimal stopLoss;
        decimal target1;
        decimal target2;

        if (primary.Direction == MarketDirection.Bullish)
        {
            direction = "LONG";

            stopLoss = primary.StructureStopLoss > 0m
                ? primary.StructureStopLoss
                : entry - atrRiskDistance;

            target1 = primary.StructureTarget1 > entry
                ? primary.StructureTarget1
                : entry + atrRiskDistance;

            target2 = primary.StructureTarget2 > target1
                ? primary.StructureTarget2
                : entry + atrRiskDistance * 2m;

            if (stopLoss <= 0 || stopLoss >= entry || target1 <= entry || target2 <= target1)
                return null;
        }
        else
        {
            direction = "SHORT";

            stopLoss = primary.StructureStopLoss > 0m
                ? primary.StructureStopLoss
                : entry + atrRiskDistance;

            target1 = primary.StructureTarget1 > 0m && primary.StructureTarget1 < entry
                ? primary.StructureTarget1
                : entry - atrRiskDistance;

            target2 = primary.StructureTarget2 > 0m && primary.StructureTarget2 < target1
                ? primary.StructureTarget2
                : entry - atrRiskDistance * 2m;

            if (stopLoss <= entry || target1 <= 0 || target2 <= 0 || target2 >= target1)
                return null;
        }

        decimal risk =
            Math.Abs(
                entry - stopLoss);

        if (risk <= 0)
            return null;

        decimal reward1 =
            Math.Abs(
                target1 - entry);

        decimal reward2 =
            Math.Abs(
                target2 - entry);

        decimal rr1 = reward1 / risk;
        decimal rr2 = reward2 / risk;

        if (rr1 < settings.MinimumRiskReward &&
            rr2 < settings.MinimumRiskReward)
        {
            return null;
        }

        return new PaperSetup
        {
            Coin = symbol,
            Timeframe = primary.Timeframe,
            Direction = direction,
            Entry = entry,
            StopLoss = stopLoss,
            Target1 = target1,
            Target2 = target2,
            RiskRewardTarget1 =
                rr1,
            RiskRewardTarget2 =
                rr2,
            Confidence =
                primary.Confidence,
            Timestamp =
                primary.Timestamp
        };
    }

    private static async Task TryCreatePaperSetup(
        string symbol,
        TimeframeAnalysis primary,
        List<PaperTrade> paperHistory,
        List<PaperTrade> paperJournal,
        PaperAccount paperAccount,
        IndicatorSettings indicators)
    {
        PaperSetup? setup =
            BuildPaperSetup(
                symbol,
                primary,
                indicators);

        if (setup == null)

            return;

            // =============================
// PAPER TRADE USER CONFIRMATION
// =============================

decimal riskDistance =
    Math.Abs(setup.Entry - setup.StopLoss);

if (riskDistance <= 0)
    return;

// Simulation के लिए 1% balance को maximum risk माना गया है.
decimal riskBudget =
    paperAccount.Balance * 0.01m;

// Quantity का सुझाव
decimal suggestedUnits =
    riskBudget / riskDistance;

if (suggestedUnits <= 0)
    return;

decimal suggestedAmount =
    suggestedUnits * setup.Entry;

Console.WriteLine();
Console.WriteLine("==========================================");
Console.WriteLine("PAPER TRADE SIGNAL");
Console.WriteLine("==========================================");
Console.WriteLine($"Coin        : {setup.Coin}");
Console.WriteLine($"Direction   : {setup.Direction}");
Console.WriteLine($"Entry       : {setup.Entry:F4}");
Console.WriteLine($"Stop Loss   : {setup.StopLoss:F4}");
Console.WriteLine($"Target 1    : {setup.Target1:F4}");
Console.WriteLine($"Target 2    : {setup.Target2:F4}");
Console.WriteLine($"Confidence  : {setup.Confidence:F0}/100");
Console.WriteLine();
Console.WriteLine($"Suggested Units : {suggestedUnits:F6}");
Console.WriteLine($"Suggested Amount: {suggestedAmount:F2}");
Console.WriteLine();
Console.Write("Trade leni hai? (Y/N): ");

string decision =
    (Console.ReadLine() ?? string.Empty)
        .Trim()
        .ToUpperInvariant();

if (decision != "Y")
{
    Console.WriteLine("Paper trade SKIPPED.");
    return;
}

Console.WriteLine();
Console.Write("Kitne units lene hain? (Enter = suggested): ");

string quantityInput =
    (Console.ReadLine() ?? string.Empty).Trim();

decimal units;

if (string.IsNullOrWhiteSpace(quantityInput))
{
    units = suggestedUnits;
}
else if (!decimal.TryParse(
             quantityInput,
             out units) ||
         units <= 0)
{
    Console.WriteLine("Invalid quantity. Trade SKIPPED.");
    return;
}

Console.Write("Kitne minute monitor karna hai? ");

string minutesInput =
    (Console.ReadLine() ?? string.Empty).Trim();

if (!int.TryParse(
        minutesInput,
        out int monitoringMinutes) ||
    monitoringMinutes <= 0)
{
    Console.WriteLine("Invalid monitoring time. Trade SKIPPED.");
    return;
}

decimal investedAmount =
    units * setup.Entry;

if (investedAmount > paperAccount.Balance)
{
    Console.WriteLine(
        "Insufficient paper balance. Trade SKIPPED.");

    return;
}

        bool alreadyExists =
            paperHistory.Any(
                p =>
                    p.Coin.Equals(
                        symbol,
                        StringComparison.OrdinalIgnoreCase) &&
                    p.Timeframe.Equals(
                        primary.Timeframe,
                        StringComparison.OrdinalIgnoreCase) &&
                    p.EntryCandleTimestamp ==
                        primary.Timestamp);

        if (alreadyExists)
            return;

        var trade =
            new PaperTrade
            {
                Coin = setup.Coin,
                InitialStopLoss = setup.StopLoss,
           HighestPriceSinceEntry = setup.Entry,
              LowestPriceSinceEntry = setup.Entry,
               TrailingStopDistance = 0m,
               TrailingStopActive = false,
                Timeframe = setup.Timeframe,
                Direction = setup.Direction,
                Entry = setup.Entry,
                StopLoss = setup.StopLoss,
                Target1 = setup.Target1,
                Target2 = setup.Target2,
                Confidence = setup.Confidence,
                Timestamp = DateTime.UtcNow,
                EntryCandleTimestamp =
                    setup.Timestamp,
                    InvestedAmount = investedAmount,
                    RemainingInvestedAmount = investedAmount,
                    RemainingUnits = units,
                    Target1Hit = false,
                    RealizedPartialPnL = 0m,
                    Units = units,

                 MonitoringMinutes = monitoringMinutes,

                UserConfirmed = true,
 
               Status = "OPEN"
            };

        paperHistory.Add(trade);
        paperJournal.Add(trade);
        paperAccount.OpenTrade(
            trade.InvestedAmount);

        _ = Notifications!.SendSetupConfirmationAsync(
            trade);

        if (BinanceClient?.IsArmed == true)
        {
            string side =
                trade.Direction.Equals(
                    "LONG",
                    StringComparison.OrdinalIgnoreCase)
                    ? "BUY"
                    : "SELL";

            try
            {
                BinanceOrderResult result =
                    await BinanceClient.PlaceLimitOrderAsync(
                        trade.Coin,
                        side,
                        trade.RemainingUnits,
                        trade.Entry);

                Console.WriteLine();
                Console.WriteLine(
                    $"{(result.IsLive ? "LIVE" : "TESTNET")} ORDER: {result.OrderId} / {result.Status}");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"BINANCE ORDER ERROR: {ex.Message}");

                await Notifications!.SendTextAsync(
                    "TESTNET ORDER ERROR",
                    $"{trade.Coin} {trade.Direction}: {ex.Message}");
            }
        }

        trade.PeakEquity = paperAccount.PeakEquity;
        trade.Drawdown = paperAccount.Drawdown;
        trade.DrawdownPercent = paperAccount.DrawdownPercent;
    }

    private static void UpdatePaperJournal(
        string symbol,
        TimeframeAnalysis primary,
        List<PaperTrade> paperHistory,
        PaperAccount paperAccount)
    {
        var openTrades =
            paperHistory
                .Where(
                    p =>
                        p.Status == "OPEN" &&
                        p.Coin.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                        p.Timeframe.Equals(primary.Timeframe, StringComparison.OrdinalIgnoreCase) &&
                        primary.Candles.Any(c => c.Timestamp > p.EntryCandleTimestamp))
                .ToList();

        foreach (PaperTrade trade in openTrades)
        {
            IEnumerable<Candle> laterCandles =
                primary.Candles
                    .Where(c => c.Timestamp > trade.EntryCandleTimestamp)
                    .OrderBy(c => c.Timestamp);

            foreach (Candle candle in laterCandles)
            {
                DateTime expiry =
                    trade.Timestamp.AddMinutes(Math.Max(1, trade.MonitoringMinutes));

                bool expired =
                    trade.MonitoringMinutes > 0 &&
                    candle.Timestamp >= expiry;

                trade.PeakEquity = Math.Max(
                    trade.PeakEquity,
                    paperAccount.Equity);
                trade.Drawdown = Math.Max(
                    0m,
                    trade.PeakEquity - paperAccount.Equity);
                trade.DrawdownPercent = trade.PeakEquity > 0m
                    ? trade.Drawdown * 100m / trade.PeakEquity
                    : 0m;

                // Conservative OHLC handling: if both the stop and a target
                // are touched in the same candle, intrabar order is unknown.
                // Do not invent an order; reconcile the reserved capital once.
                if (trade.Direction == "LONG")
                {
                    trade.HighestPriceSinceEntry =
                        Math.Max(trade.HighestPriceSinceEntry, candle.High);

                    decimal initialRisk =
                        Math.Abs(trade.Entry - trade.InitialStopLoss);

                    if (initialRisk <= 0m)
                        initialRisk = Math.Abs(trade.Entry - trade.StopLoss);

                    if (!trade.TrailingStopActive &&
                        initialRisk > 0m &&
                        trade.HighestPriceSinceEntry >= trade.Entry + initialRisk)
                    {
                        trade.TrailingStopActive = true;
                        trade.TrailingStopDistance = initialRisk;
                    }

                    if (trade.TrailingStopActive && trade.TrailingStopDistance > 0m)
                    {
                        decimal newStop =
                            trade.HighestPriceSinceEntry - trade.TrailingStopDistance;

                        if (newStop > trade.StopLoss && newStop < candle.High)
                            trade.StopLoss = newStop;
                    }

                    bool stopHit = candle.Low <= trade.StopLoss;
                    bool target1Hit = !trade.Target1Hit && candle.High >= trade.Target1;
                    bool target2Hit = trade.Target1Hit && candle.High >= trade.Target2;

                    if (stopHit && (target1Hit || target2Hit))
                    {
                        MarkAmbiguousTrade(trade, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (stopHit)
                    {
                        ClosePaperTrade(trade, "LOSS", trade.StopLoss, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (target1Hit)
                    {
                        ApplyTarget1PartialClose(trade, trade.Target1, candle.Timestamp, paperAccount);
                    }

                    if (trade.Status == "OPEN" && target2Hit)
                    {
                        ClosePaperTrade(trade, "WIN", trade.Target2, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (expired && trade.Status == "OPEN")
                    {
                        ClosePaperTrade(trade, "TIMEOUT", candle.Close, candle.Timestamp, paperAccount);
                        break;
                    }
                }
                else if (trade.Direction == "SHORT")
                {
                    if (trade.LowestPriceSinceEntry == 0m)
                        trade.LowestPriceSinceEntry = trade.Entry;

                    trade.LowestPriceSinceEntry =
                        Math.Min(trade.LowestPriceSinceEntry, candle.Low);

                    decimal initialRisk =
                        Math.Abs(trade.Entry - trade.InitialStopLoss);

                    if (initialRisk <= 0m)
                        initialRisk = Math.Abs(trade.Entry - trade.StopLoss);

                    if (!trade.TrailingStopActive &&
                        initialRisk > 0m &&
                        trade.LowestPriceSinceEntry <= trade.Entry - initialRisk)
                    {
                        trade.TrailingStopActive = true;
                        trade.TrailingStopDistance = initialRisk;
                    }

                    if (trade.TrailingStopActive && trade.TrailingStopDistance > 0m)
                    {
                        decimal newStop =
                            trade.LowestPriceSinceEntry + trade.TrailingStopDistance;

                        if (newStop < trade.StopLoss && newStop > candle.Low)
                            trade.StopLoss = newStop;
                    }

                    bool stopHit = candle.High >= trade.StopLoss;
                    bool target1Hit = !trade.Target1Hit && candle.Low <= trade.Target1;
                    bool target2Hit = trade.Target1Hit && candle.Low <= trade.Target2;

                    if (stopHit && (target1Hit || target2Hit))
                    {
                        MarkAmbiguousTrade(trade, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (stopHit)
                    {
                        ClosePaperTrade(trade, "LOSS", trade.StopLoss, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (target1Hit)
                    {
                        ApplyTarget1PartialClose(trade, trade.Target1, candle.Timestamp, paperAccount);
                    }

                    if (trade.Status == "OPEN" && target2Hit)
                    {
                        ClosePaperTrade(trade, "WIN", trade.Target2, candle.Timestamp, paperAccount);
                        break;
                    }

                    if (expired && trade.Status == "OPEN")
                    {
                        ClosePaperTrade(trade, "TIMEOUT", candle.Close, candle.Timestamp, paperAccount);
                        break;
                    }
                }
            }
        }
    }

    private static void ApplyTarget1PartialClose(
        PaperTrade trade,
        decimal outcomePrice,
        DateTime timestamp,
        PaperAccount paperAccount)
    {
        if (trade.Status != "OPEN" || trade.Target1Hit)
            return;

        decimal remainingInvested =
            trade.RemainingInvestedAmount > 0m
                ? trade.RemainingInvestedAmount
                : trade.InvestedAmount;

        decimal partialInvested =
            remainingInvested * 0.50m;

        if (partialInvested <= 0m)
            return;

        decimal priceChange =
            trade.Direction.Equals("LONG", StringComparison.OrdinalIgnoreCase)
                ? (outcomePrice - trade.Entry) / trade.Entry
                : (trade.Entry - outcomePrice) / trade.Entry;

        decimal partialPnl =
            partialInvested * priceChange;

        trade.RealizedPartialPnL += partialPnl;
        trade.Target1Hit = true;
        trade.RemainingInvestedAmount =
            remainingInvested - partialInvested;
        trade.RemainingUnits =
            trade.RemainingInvestedAmount > 0m
                ? trade.RemainingInvestedAmount / trade.Entry
                : 0m;

        paperAccount.ReleasePartialTrade(
            partialInvested,
            partialPnl);

        trade.SimulatedPnL =
            trade.RealizedPartialPnL;

        trade.SimulatedProfitLoss =
            trade.RealizedPartialPnL;
    }

    private static void MarkAmbiguousTrade(
        PaperTrade trade,
        DateTime timestamp,
        PaperAccount paperAccount)
    {
        if (trade.Status != "OPEN")
            return;

        decimal reserved =
            trade.RemainingInvestedAmount > 0m
                ? trade.RemainingInvestedAmount
                : trade.InvestedAmount;

        trade.Status = "AMBIGUOUS";
        trade.ClosedTimestamp = timestamp;
        trade.OutcomePrice = null;
        trade.SimulatedPnL = trade.RealizedPartialPnL;
        trade.SimulatedProfitLoss = trade.RealizedPartialPnL;
        trade.RemainingInvestedAmount = 0m;
        trade.RemainingUnits = 0m;

        // Ambiguous means no invented P/L for the unresolved portion, but the
        // reserved paper capital must still be released exactly once.
        paperAccount.ReleaseAmbiguousTrade(reserved);

        _ = Notifications!.SendClosureAsync(
            trade,
            "AMBIGUOUS",
            null);
    }

    private static void ClosePaperTrade(
        PaperTrade trade,
        string status,
        decimal outcomePrice,
        DateTime closedTimestamp,
        PaperAccount paperAccount)
    {
        if (trade.Status != "OPEN")
            return;

        decimal settlementInvested =
            trade.RemainingInvestedAmount > 0m
                ? trade.RemainingInvestedAmount
                : trade.InvestedAmount;

        if (settlementInvested <= 0m)
            settlementInvested = trade.InvestedAmount;

        decimal priceChange =
            trade.Direction.Equals("LONG", StringComparison.OrdinalIgnoreCase)
                ? (outcomePrice - trade.Entry) / trade.Entry
                : (trade.Entry - outcomePrice) / trade.Entry;

        decimal finalPnl = settlementInvested * priceChange;
        decimal totalPnl = trade.RealizedPartialPnL + finalPnl;

        trade.Status = totalPnl >= 0m ? "WIN" : "LOSS";
        trade.ClosedTimestamp = closedTimestamp;
        trade.OutcomePrice = outcomePrice;
        trade.SimulatedPnL = totalPnl;
        trade.SimulatedProfitLoss = totalPnl;
        trade.RemainingInvestedAmount = 0m;
        trade.RemainingUnits = 0m;

        paperAccount.CloseTrade(
            settlementInvested,
            finalPnl,
            trade.Status);

        _ = Notifications!.SendClosureAsync(
            trade,
            trade.Status,
            outcomePrice);
    }

    private static decimal CalculateSimulatedPnL(
        string direction,
        decimal entry,
        decimal exit)
    {
        return direction.Equals(
                "LONG",
                StringComparison.OrdinalIgnoreCase)
            ? exit - entry
            : entry - exit;
    }

    private static void PrintLastCandles(
        TimeframeAnalysis primary)
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("LAST 10 PRIMARY CANDLES");
        Console.WriteLine("------------------------------------------");

        foreach (Candle candle in
                 primary.Candles.TakeLast(10))
        {
            Console.WriteLine(
                $"{candle.Timestamp:MM-dd HH:mm} | " +
                $"O:{FormatPrice(candle.Open)} " +
                $"H:{FormatPrice(candle.High)} " +
                $"L:{FormatPrice(candle.Low)} " +
                $"C:{FormatPrice(candle.Close)} " +
                $"V:{FormatDecimal(candle.Volume)}");
        }
    }

    private static void PrintScanHistory(
        List<ScanHistoryRecord> history)
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("RECENT SCAN HISTORY");
        Console.WriteLine("------------------------------------------");

        if (history.Count == 0)
        {
            Console.WriteLine(
                "No scans recorded yet.");

            return;
        }

        foreach (ScanHistoryRecord record in
                 history
                     .TakeLast(10)
                     .Reverse())
        {
            Console.WriteLine(
                $"{record.Timestamp:HH:mm:ss} | " +
                $"{record.Coin,-9} | " +
                $"{record.PrimaryTimeframe,-3} | " +
                $"{FormatPrice(record.Price),12} | " +
                $"{record.Signal,-22} | " +
                $"{record.Confidence:F0}/100");
        }
    }

    private static void PrintPaperJournal(
        List<PaperTrade> journal)
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("PAPER TRADE JOURNAL");
        Console.WriteLine("------------------------------------------");

        if (journal.Count == 0)
        {
            Console.WriteLine(
                "No paper setups recorded.");

            return;
        }

        foreach (PaperTrade trade in
                 journal
                     .TakeLast(10)
                     .Reverse())
        {
            Console.WriteLine(
                $"{trade.Timestamp:HH:mm:ss} | " +
                $"{trade.Coin,-9} | " +
                $"{trade.Timeframe,-3} | " +
                $"{trade.Direction,-5} | " +
                $"E:{FormatPrice(trade.Entry)} | " +
                $"SL:{FormatPrice(trade.StopLoss)} | " +
                $"T:{FormatPrice(trade.Target1)} | " +
                $"{trade.Confidence:F0} | " +
                $"{trade.Status} | " +
                $"PnL:{trade.SimulatedPnL:F4}");
        }
    }

    private static void PrintPerformance(
        List<PaperTrade> history)
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("PAPER PERFORMANCE — SESSION");
        Console.WriteLine("------------------------------------------");

        int total =
            history.Count;

        int wins =
            history.Count(
                p => p.Status == "WIN");

        int losses =
            history.Count(
                p => p.Status == "LOSS");

        int open =
            history.Count(
                p => p.Status == "OPEN");

        int ambiguous =
            history.Count(
                p => p.Status == "AMBIGUOUS");

        int resolved =
            wins + losses;

        decimal winRate =
            resolved > 0
                ? wins * 100m / resolved
                : 0m;

        decimal averageConfidence =
            total > 0
                ? history.Average(
                    p => p.Confidence)
                : 0m;

        decimal pnl =
            history
                .Where(
                    p =>
                        p.Status == "WIN" ||
                        p.Status == "LOSS")
                .Sum(
                    p => p.SimulatedPnL);

        Console.WriteLine(
            $"Total setups       : {total}");

        Console.WriteLine(
            $"Winning setups     : {wins}");

        Console.WriteLine(
            $"Losing setups      : {losses}");

        Console.WriteLine(
            $"Open setups        : {open}");

        Console.WriteLine(
            $"Ambiguous setups   : {ambiguous}");

        Console.WriteLine(
            $"Resolved setups    : {resolved}");

        Console.WriteLine(
            $"Win rate           : {winRate:F2}%");

        Console.WriteLine(
            $"Simulated P/L      : {pnl:F4}");

        Console.WriteLine(
            $"Average confidence : {averageConfidence:F2}/100");

        Console.WriteLine();
        Console.WriteLine(
            "P/L is price-unit simulation because no position quantity was specified.");

        Console.WriteLine(
            "Session statistics are paper-analysis results only.");
    }

    private static void PrintV7PerformanceReport(
        List<PaperTrade> history,
        PaperAccount paperAccount)
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("V7 PERFORMANCE REPORT — 20 DAY WINDOW");
        Console.WriteLine("------------------------------------------");

        DateTime cutoff =
            DateTime.UtcNow.AddDays(-20);

        List<PaperTrade> trades =
            history
                .Where(
                    t => t.Timestamp >= cutoff)
                .OrderBy(
                    t => t.Timestamp)
                .ToList();

        if (trades.Count == 0)
        {
            Console.WriteLine(
                "No trades available for the 20-day report.");

            return;
        }

        int total =
            trades.Count;

        int wins =
            trades.Count(
                t => t.Status == "WIN");

        int losses =
            trades.Count(
                t => t.Status == "LOSS");

        int open =
            trades.Count(
                t => t.Status == "OPEN");

        int ambiguous =
            trades.Count(
                t => t.Status == "AMBIGUOUS");

        int closed =
            wins + losses;

        decimal winRate =
            closed > 0
                ? wins * 100m / closed
                : 0m;

        decimal pnl =
            trades
                .Where(
                    t =>
                        t.Status == "WIN" ||
                        t.Status == "LOSS")
                .Sum(
                    t => t.SimulatedPnL);

        decimal grossProfit =
            trades
                .Where(t => t.Status == "WIN" && t.SimulatedPnL > 0)
                .Sum(t => t.SimulatedPnL);

        decimal grossLoss =
            trades
                .Where(t => t.Status == "LOSS" && t.SimulatedPnL < 0)
                .Sum(t => Math.Abs(t.SimulatedPnL));

        decimal profitFactor =
            grossLoss > 0
                ? grossProfit / grossLoss
                : grossProfit > 0
                    ? decimal.MaxValue
                    : 0m;

        decimal expectancy =
            closed > 0
                ? pnl / closed
                : 0m;

        decimal equity = paperAccount.StartingBalance;
        decimal peakEquity = equity;
        decimal maxDrawdown = 0m;
        decimal maxDrawdownPercent = 0m;

        foreach (PaperTrade trade in trades)
        {
            if (trade.Status != "WIN" &&
                trade.Status != "LOSS")
            {
                continue;
            }

            equity +=
                trade.SimulatedPnL;

            if (equity > peakEquity)
            {
                peakEquity =
                    equity;
            }

            decimal drawdown =
                peakEquity - equity;

            if (drawdown > maxDrawdown)
            {
                maxDrawdown =
                    drawdown;
            }

            if (peakEquity > 0)
            {
                decimal drawdownPercent =
                    drawdown *
                    100m /
                    peakEquity;

                if (drawdownPercent >
                    maxDrawdownPercent)
                {
                    maxDrawdownPercent =
                        drawdownPercent;
                }
            }
        }

        Console.WriteLine(
            "Period          : Last 20 days");

        Console.WriteLine(
            $"Total setups    : {total}");

        Console.WriteLine(
            $"Wins            : {wins}");

        Console.WriteLine(
            $"Losses          : {losses}");

        Console.WriteLine(
            $"Open            : {open}");

        Console.WriteLine(
            $"Ambiguous       : {ambiguous}");

        Console.WriteLine(
            $"Resolved        : {closed}");

        Console.WriteLine(
            $"Win rate        : {winRate:F2}%");

        Console.WriteLine(
            $"Simulated P/L   : {pnl:F4}");

        Console.WriteLine(
            $"Profit Factor   : {(profitFactor == decimal.MaxValue ? "INF" : profitFactor.ToString("F4", CultureInfo.InvariantCulture))}");

        Console.WriteLine(
            $"Expectancy      : {expectancy:F4}");

        Console.WriteLine(
            $"Max Drawdown    : {maxDrawdown:F4}");

        Console.WriteLine(
            $"Max DD %        : {maxDrawdownPercent:F2}%");

        Console.WriteLine(
            $"Current DD %     : {paperAccount.DrawdownPercent:F2}%");

        Console.WriteLine();
        Console.WriteLine(
            "PAPER ANALYSIS ONLY — NO REAL MONEY");
    }

    private static void LoadPaperAccount(
        string file,
        PaperAccount account,
        List<PaperTrade> history)
    {
        bool loaded = false;

        try
        {
            if (File.Exists(file))
            {
                foreach (string line in File.ReadLines(file))
                {
                    if (!line.StartsWith("ACCOUNT|", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] parts = line.Split('|', StringSplitOptions.TrimEntries);

                    account.StartingBalance = ReadAccountDecimal(parts, "StartingBalance=", account.StartingBalance);
                    account.Balance = ReadAccountDecimal(parts, "Balance=", account.Balance);
                    account.UsedBalance = ReadAccountDecimal(parts, "UsedBalance=", account.UsedBalance);
                    account.RealizedProfit = ReadAccountDecimal(parts, "RealizedProfit=", account.RealizedProfit);
                    account.RealizedLoss = ReadAccountDecimal(parts, "RealizedLoss=", account.RealizedLoss);
                    account.TotalTrades = ReadAccountInt(parts, "TotalTrades=", account.TotalTrades);
                    account.WinningTrades = ReadAccountInt(parts, "WinningTrades=", account.WinningTrades);
                    account.LosingTrades = ReadAccountInt(parts, "LosingTrades=", account.LosingTrades);
                    account.PeakEquity = ReadAccountDecimal(parts, "PeakEquity=", account.PeakEquity);
                    account.Drawdown = ReadAccountDecimal(parts, "Drawdown=", account.Drawdown);
                    account.DrawdownPercent = ReadAccountDecimal(parts, "DrawdownPercent=", account.DrawdownPercent);
                    loaded = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Account load error: {ex.Message}");
        }

        if (!loaded)
            RebuildPaperAccountFromHistory(account, history);
    }

    private static void RebuildPaperAccountFromHistory(
        PaperAccount account,
        List<PaperTrade> history)
    {
        decimal starting = account.StartingBalance;
        decimal realized = history
            .Where(t => t.Status == "WIN" || t.Status == "LOSS")
            .Sum(t => t.SimulatedPnL);

        decimal used = history
            .Where(t => t.Status == "OPEN")
            .Sum(t => Math.Max(0m, t.InvestedAmount));

        account.Balance = Math.Max(0m, starting + realized - used);
        account.UsedBalance = used;
        account.RealizedProfit = history
            .Where(t => t.Status == "WIN" && t.SimulatedPnL > 0m)
            .Sum(t => t.SimulatedPnL);
        account.RealizedLoss = history
            .Where(t => t.Status == "LOSS" && t.SimulatedPnL < 0m)
            .Sum(t => Math.Abs(t.SimulatedPnL));
        account.TotalTrades = history.Count(t => t.UserConfirmed);
        account.WinningTrades = history.Count(t => t.Status == "WIN");
        account.LosingTrades = history.Count(t => t.Status == "LOSS");
        account.PeakEquity = Math.Max(starting, account.Balance + account.UsedBalance);
        account.Drawdown = Math.Max(0m, account.PeakEquity - (account.Balance + account.UsedBalance));
        account.DrawdownPercent = account.PeakEquity > 0m
            ? account.Drawdown * 100m / account.PeakEquity
            : 0m;
    }

    private static decimal ReadAccountDecimal(
        string[] parts, string prefix, decimal fallback)
    {
        return TryReadPrefixedDecimal(parts, prefix, out decimal value) ? value : fallback;
    }

    private static int ReadAccountInt(
        string[] parts, string prefix, int fallback)
    {
        return TryReadPrefixedInt(parts, prefix, out int value) ? value : fallback;
    }

    private static List<PaperTrade> LoadV7History(
        string file)
    {
        var history =
            new List<PaperTrade>();

        try
        {
            if (!File.Exists(file))
                return history;

            foreach (string line in
                     File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith(
                        "NEON FUTURES",
                        StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith(
                        "====",
                        StringComparison.Ordinal) ||
                    line.StartsWith(
                        "ACCOUNT|",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] parts =
                    line.Split(
                        '|',
                        StringSplitOptions.TrimEntries);

                if (parts.Length < 9)
                    continue;

                if (!DateTime.TryParse(
                        parts[0],
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal,
                        out DateTime timestamp))
                {
                    continue;
                }

                string coin =
                    parts[1];

                string timeframe =
                    parts[2];

                string direction =
                    parts[3];

                if (!TryReadPrefixedDecimal(
                        parts,
                        "Entry=",
                        out decimal entry))
                {
                    continue;
                }

                if (!TryReadPrefixedDecimal(
                        parts,
                        "SL=",
                        out decimal stopLoss))
                {
                    continue;
                }

                if (!TryReadPrefixedDecimal(
                        parts,
                        "Target=",
                        out decimal target1))
                {
                    continue;
                }

                string status =
                    ReadPrefixedString(
                        parts,
                        "Status=",
                        "OPEN");

                decimal pnl =
                    TryReadPrefixedDecimal(
                        parts,
                        "PnL=",
                        out decimal parsedPnl)
                        ? parsedPnl
                        : 0m;
                        
                        decimal simulatedProfitLoss =
    TryReadPrefixedDecimal(
        parts,
        "SimulatedProfitLoss=",
        out decimal parsedSimulatedProfitLoss)
        ? parsedSimulatedProfitLoss
        : pnl;

decimal units =
    TryReadPrefixedDecimal(
        parts,
        "Units=",
        out decimal parsedUnits)
        ? parsedUnits
        : 0m;

decimal investedAmount =
    TryReadPrefixedDecimal(
        parts,
        "InvestedAmount=",
        out decimal parsedInvestedAmount)
        ? parsedInvestedAmount
        : 0m;

decimal remainingInvestedAmount =
    TryReadPrefixedDecimal(
        parts,
        "RemainingInvestedAmount=",
        out decimal parsedRemainingInvestedAmount)
        ? parsedRemainingInvestedAmount
        : (status == "OPEN" ? investedAmount : 0m);

decimal remainingUnits =
    TryReadPrefixedDecimal(
        parts,
        "RemainingUnits=",
        out decimal parsedRemainingUnits)
        ? parsedRemainingUnits
        : (remainingInvestedAmount > 0m && entry > 0m ? remainingInvestedAmount / entry : units);

bool target1Hit =
    TryReadPrefixedBool(
        parts,
        "Target1Hit=",
        out bool parsedTarget1Hit)
        ? parsedTarget1Hit
        : false;

decimal realizedPartialPnL =
    TryReadPrefixedDecimal(
        parts,
        "RealizedPartialPnL=",
        out decimal parsedRealizedPartialPnL)
        ? parsedRealizedPartialPnL
        : 0m;

int monitoringMinutes =
    TryReadPrefixedInt(
        parts,
        "MonitoringMinutes=",
        out int parsedMonitoringMinutes)
        ? parsedMonitoringMinutes
        : 0;

bool userConfirmed =
    TryReadPrefixedBool(
        parts,
        "UserConfirmed=",
        out bool parsedUserConfirmed)
        ? parsedUserConfirmed
        : false;

decimal initialStopLoss =
    TryReadPrefixedDecimal(
        parts,
        "InitialStopLoss=",
        out decimal parsedInitialStopLoss)
        ? parsedInitialStopLoss
        : stopLoss;

decimal highestPriceSinceEntry =
    TryReadPrefixedDecimal(
        parts,
        "HighestPriceSinceEntry=",
        out decimal parsedHighestPrice)
        ? parsedHighestPrice
        : entry;

decimal lowestPriceSinceEntry =
    TryReadPrefixedDecimal(
        parts,
        "LowestPriceSinceEntry=",
        out decimal parsedLowestPrice)
        ? parsedLowestPrice
        : entry;

decimal trailingStopDistance =
    TryReadPrefixedDecimal(
        parts,
        "TrailingStopDistance=",
        out decimal parsedTrailingDistance)
        ? parsedTrailingDistance
        : 0m;

bool trailingStopActive =
    TryReadPrefixedBool(
        parts,
        "TrailingStopActive=",
        out bool parsedTrailingActive)
        ? parsedTrailingActive
        : false;

decimal peakEquity =
    TryReadPrefixedDecimal(
        parts,
        "PeakEquity=",
        out decimal parsedPeakEquity)
        ? parsedPeakEquity
        : 0m;

decimal drawdown =
    TryReadPrefixedDecimal(
        parts,
        "Drawdown=",
        out decimal parsedDrawdown)
        ? parsedDrawdown
        : 0m;

decimal drawdownPercent =
    TryReadPrefixedDecimal(
        parts,
        "DrawdownPercent=",
        out decimal parsedDrawdownPercent)
        ? parsedDrawdownPercent
        : 0m;

                DateTime entryCandle =
                    timestamp;

                if (TryReadPrefixedDateTime(
                        parts,
                        "EntryCandle=",
                        out DateTime savedEntryCandle))
                {
                    entryCandle =
                        savedEntryCandle;
                }

                DateTime? closedTimestamp =
                    null;

                if (TryReadPrefixedDateTime(
                        parts,
                        "Closed=",
                        out DateTime savedClosed))
                {
                    closedTimestamp =
                        savedClosed;
                }

                decimal? outcomePrice =
                    null;

                if (TryReadPrefixedDecimal(
                        parts,
                        "Outcome=",
                        out decimal savedOutcome))
                {
                    outcomePrice =
                        savedOutcome;
                }

                decimal confidence =
                    TryReadPrefixedDecimal(
                        parts,
                        "Confidence=",
                        out decimal savedConfidence)
                        ? savedConfidence
                        : 0m;

                history.Add(
                    new PaperTrade
                    {
                        Coin = coin,
                        Timeframe = timeframe,
                        Direction = direction,
                        Entry = entry,
                        StopLoss = stopLoss,
                        Target1 = target1,

Target2 =
    TryReadPrefixedDecimal(
        parts,
        "Target2=",
        out decimal parsedTarget2)
        ? parsedTarget2
        : direction.Equals(
            "LONG",
            StringComparison.OrdinalIgnoreCase)
            
                                ? entry +
                                  Math.Abs(
                                      target1 - entry) * 2m
                                : entry -
                                  Math.Abs(
                                      target1 - entry) * 2m,
                        Confidence = confidence,
                        Timestamp = timestamp,
                        EntryCandleTimestamp =
                            entryCandle,
                        Status = status,
                        ClosedTimestamp =
                            closedTimestamp,
                        OutcomePrice =
                            outcomePrice,
                        SimulatedPnL = pnl,
                        SimulatedProfitLoss = simulatedProfitLoss,
Units = units,
InvestedAmount = investedAmount,
RemainingInvestedAmount = remainingInvestedAmount,
RemainingUnits = remainingUnits,
Target1Hit = target1Hit,
RealizedPartialPnL = realizedPartialPnL,
MonitoringMinutes = monitoringMinutes,
UserConfirmed = userConfirmed,
InitialStopLoss = initialStopLoss,
HighestPriceSinceEntry = highestPriceSinceEntry,
LowestPriceSinceEntry = lowestPriceSinceEntry,
TrailingStopDistance = trailingStopDistance,
TrailingStopActive = trailingStopActive,
PeakEquity = peakEquity,
Drawdown = drawdown,
DrawdownPercent = drawdownPercent
                    });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"History load error: {ex.Message}");
            Console.WriteLine();
        }

        return history
            .OrderBy(
                t => t.Timestamp)
            .ToList();
    }

    private static bool TryReadPrefixedDecimal(
        string[] parts,
        string prefix,
        out decimal value)
    {
        value = 0m;

        string? text =
            parts
                .FirstOrDefault(
                    p => p.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase));

        if (text == null)
            return false;

        string number =
            text[prefix.Length..]
                .Trim();

        return decimal.TryParse(
            number,
            NumberStyles.Number |
            NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out value);
    }
    private static bool TryReadPrefixedInt(
    string[] parts,
    string prefix,
    out int value)
{
    value = 0;

    string? text =
        parts
            .FirstOrDefault(
                p => p.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));

    if (text == null)
        return false;

    string number =
        text[prefix.Length..]
            .Trim();

    return int.TryParse(
        number,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out value);
}
private static bool TryReadPrefixedBool(
    string[] parts,
    string prefix,
    out bool value)
{
    value = false;

    string? text =
        parts
            .FirstOrDefault(
                p => p.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));

    if (text == null)
        return false;

    string boolean =
        text[prefix.Length..]
            .Trim();

    return bool.TryParse(
        boolean,
        out value);
}

    private static string ReadPrefixedString(
        string[] parts,
        string prefix,
        string fallback)
    {
        string? text =
            parts
                .FirstOrDefault(
                    p => p.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase));

        if (text == null)
            return fallback;

        return text[prefix.Length..]
            .Trim();
    }

    private static bool TryReadPrefixedDateTime(
        string[] parts,
        string prefix,
        out DateTime value)
    {
        value = default;

        string? text =
            parts
                .FirstOrDefault(
                    p => p.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase));

        if (text == null)
            return false;

        return DateTime.TryParse(
            text[prefix.Length..].Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static void SaveV7History(
        List<PaperTrade> history,
        PaperAccount account)
    {
        string tempFile =
            HistoryFile + ".tmp";

        try
        {
            using (
                var writer =
                    new StreamWriter(
                        tempFile,
                        false,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(
                    "NEON FUTURES SCANNER V7 HISTORY");

                writer.WriteLine(
                    "==========================================");

                writer.WriteLine(
                    "ACCOUNT|" +
                    $"StartingBalance={account.StartingBalance.ToString(CultureInfo.InvariantCulture)} | " +
                    $"Balance={account.Balance.ToString(CultureInfo.InvariantCulture)} | " +
                    $"UsedBalance={account.UsedBalance.ToString(CultureInfo.InvariantCulture)} | " +
                    $"RealizedProfit={account.RealizedProfit.ToString(CultureInfo.InvariantCulture)} | " +
                    $"RealizedLoss={account.RealizedLoss.ToString(CultureInfo.InvariantCulture)} | " +
                    $"TotalTrades={account.TotalTrades} | " +
                    $"WinningTrades={account.WinningTrades} | " +
                    $"LosingTrades={account.LosingTrades} | " +
                    $"PeakEquity={account.PeakEquity.ToString(CultureInfo.InvariantCulture)} | " +
                    $"Drawdown={account.Drawdown.ToString(CultureInfo.InvariantCulture)} | " +
                    $"DrawdownPercent={account.DrawdownPercent.ToString(CultureInfo.InvariantCulture)}");

                foreach (PaperTrade trade in
                         history.OrderBy(
                             t => t.Timestamp))
                {
                    writer.WriteLine(
    $"{trade.Timestamp:O} | " +
    $"{trade.Coin} | " +
    $"{trade.Timeframe} | " +
    $"{trade.Direction} | " +
    $"Entry={trade.Entry.ToString(CultureInfo.InvariantCulture)} | " +
    $"SL={trade.StopLoss.ToString(CultureInfo.InvariantCulture)} | " +
    $"Target={trade.Target1.ToString(CultureInfo.InvariantCulture)} | " +
    $"Target2={trade.Target2.ToString(CultureInfo.InvariantCulture)} | " +
    $"Status={trade.Status} | " +
    $"PnL={trade.SimulatedPnL.ToString(CultureInfo.InvariantCulture)} | " +
    $"SimulatedProfitLoss={trade.SimulatedProfitLoss.ToString(CultureInfo.InvariantCulture)} | " +
    $"Units={trade.Units.ToString(CultureInfo.InvariantCulture)} | " +
    $"InvestedAmount={trade.InvestedAmount.ToString(CultureInfo.InvariantCulture)} | " +
    $"RemainingInvestedAmount={trade.RemainingInvestedAmount.ToString(CultureInfo.InvariantCulture)} | " +
    $"RemainingUnits={trade.RemainingUnits.ToString(CultureInfo.InvariantCulture)} | " +
    $"Target1Hit={trade.Target1Hit} | " +
    $"RealizedPartialPnL={trade.RealizedPartialPnL.ToString(CultureInfo.InvariantCulture)} | " +
    $"MonitoringMinutes={trade.MonitoringMinutes} | " +
    $"UserConfirmed={trade.UserConfirmed} | " +
    $"InitialStopLoss={trade.InitialStopLoss.ToString(CultureInfo.InvariantCulture)} | " +
    $"HighestPriceSinceEntry={trade.HighestPriceSinceEntry.ToString(CultureInfo.InvariantCulture)} | " +
    $"LowestPriceSinceEntry={trade.LowestPriceSinceEntry.ToString(CultureInfo.InvariantCulture)} | " +
    $"TrailingStopDistance={trade.TrailingStopDistance.ToString(CultureInfo.InvariantCulture)} | " +
    $"TrailingStopActive={trade.TrailingStopActive} | " +
    $"PeakEquity={trade.PeakEquity.ToString(CultureInfo.InvariantCulture)} | " +
    $"Drawdown={trade.Drawdown.ToString(CultureInfo.InvariantCulture)} | " +
    $"DrawdownPercent={trade.DrawdownPercent.ToString(CultureInfo.InvariantCulture)} | " +
    $"EntryCandle={trade.EntryCandleTimestamp:O} | " +
    $"Closed={(trade.ClosedTimestamp.HasValue ? trade.ClosedTimestamp.Value.ToString("O") : "")} | " +
    $"Outcome={(trade.OutcomePrice.HasValue ? trade.OutcomePrice.Value.ToString(CultureInfo.InvariantCulture) : "")} | " +
    $"Confidence={trade.Confidence.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            File.Move(
                tempFile,
                HistoryFile,
                overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"History save error: {ex.Message}");

            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // Do not hide the original history-save error.
            }
        }
    }

    private static void TrimList<T>(
        List<T> list,
        int maximum)
    {
        if (maximum < 1)
            return;

        while (list.Count > maximum)
        {
            list.RemoveAt(0);
        }
    }

    private static decimal? CalculateSma(
        IReadOnlyList<decimal> values,
        int period)
    {
        if (period <= 0 ||
            values.Count < period)
        {
            return null;
        }

        decimal sum = 0m;

        for (int i = values.Count - period;
             i < values.Count;
             i++)
        {
            sum += values[i];
        }

        return sum / period;
    }

    private static decimal? CalculateEma(
        IReadOnlyList<decimal> values,
        int period)
    {
        if (period <= 0 ||
            values.Count < period)
        {
            return null;
        }

        decimal multiplier =
            2m / (period + 1m);

        decimal ema =
            values
                .Take(period)
                .Average();

        for (int i = period;
             i < values.Count;
             i++)
        {
            ema =
                ((values[i] - ema) *
                 multiplier) +
                ema;
        }

        return ema;
    }

    private static decimal? CalculateRsi(
        IReadOnlyList<decimal> values,
        int period)
    {
        if (period <= 0 ||
            values.Count <= period)
        {
            return null;
        }

        decimal gain = 0m;
        decimal loss = 0m;

        for (int i = 1;
             i <= period;
             i++)
        {
            decimal change =
                values[i] -
                values[i - 1];

            if (change > 0)
            {
                gain += change;
            }
            else
            {
                loss -= change;
            }
        }

        decimal averageGain =
            gain / period;

        decimal averageLoss =
            loss / period;

        for (int i = period + 1;
             i < values.Count;
             i++)
        {
            decimal change =
                values[i] -
                values[i - 1];

            decimal currentGain =
                Math.Max(
                    change,
                    0m);

            decimal currentLoss =
                Math.Max(
                    -change,
                    0m);

            averageGain =
                ((averageGain *
                  (period - 1m)) +
                 currentGain) /
                period;

            averageLoss =
                ((averageLoss *
                  (period - 1m)) +
                 currentLoss) /
                period;
        }

        if (averageLoss == 0)
        {
            return averageGain == 0
                ? 50m
                : 100m;
        }

        decimal rs =
            averageGain /
            averageLoss;

        return 100m -
               (100m /
                (1m + rs));
    }

    private static (
        decimal? Macd,
        decimal? Signal,
        decimal? Histogram)
        CalculateMacd(
            IReadOnlyList<decimal> values,
            int fastPeriod,
            int slowPeriod,
            int signalPeriod)
    {
        if (values.Count <
            slowPeriod + signalPeriod)
        {
            return (null, null, null);
        }

        var macdValues =
            new List<decimal>();

        for (int end = slowPeriod;
             end <= values.Count;
             end++)
        {
            decimal[] slice =
                values
                    .Take(end)
                    .ToArray();

            decimal? fast =
                CalculateEma(
                    slice,
                    fastPeriod);

            decimal? slow =
                CalculateEma(
                    slice,
                    slowPeriod);

            if (fast.HasValue &&
                slow.HasValue)
            {
                macdValues.Add(
                    fast.Value -
                    slow.Value);
            }
        }

        if (macdValues.Count <
            signalPeriod)
        {
            return (null, null, null);
        }

        decimal macd =
            macdValues[^1];

        decimal? signal =
            CalculateEma(
                macdValues,
                signalPeriod);

        if (!signal.HasValue)
        {
            return (
                macd,
                null,
                null);
        }

        return (
            macd,
            signal.Value,
            macd - signal.Value);
    }

    private static decimal? CalculateAtr(
        IReadOnlyList<Candle> candles,
        int period)
    {
        if (candles.Count <= period)
            return null;

        var trueRanges =
            new List<decimal>();

        for (int i = 1;
             i < candles.Count;
             i++)
        {
            Candle current =
                candles[i];

            Candle previous =
                candles[i - 1];

            decimal range1 =
                current.High -
                current.Low;

            decimal range2 =
                Math.Abs(
                    current.High -
                    previous.Close);

            decimal range3 =
                Math.Abs(
                    current.Low -
                    previous.Close);

            decimal trueRange =
                Math.Max(
                    range1,
                    Math.Max(
                        range2,
                        range3));

            if (trueRange >= 0)
            {
                trueRanges.Add(
                    trueRange);
            }
        }

        if (trueRanges.Count < period)
            return null;

        decimal atr =
            trueRanges
                .Take(period)
                .Average();

        for (int i = period;
             i < trueRanges.Count;
             i++)
        {
            atr =
                ((atr *
                  (period - 1m)) +
                 trueRanges[i]) /
                period;
        }

        return atr;
    }

    private static string FormatValue(
        decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString(
                "F4",
                CultureInfo.InvariantCulture)
            : "N/A";
    }

    private static string FormatPrice(
        decimal value)
    {
        int decimals =
            value >= 1000m
                ? 2
                : value >= 1m
                    ? 4
                    : 8;

        return value.ToString(
            $"F{decimals}",
            CultureInfo.InvariantCulture);
    }

    private static string FormatDecimal(
        decimal value)
    {
        return value.ToString(
            "F2",
            CultureInfo.InvariantCulture);
    }
}

public sealed record ScanHistoryRecord
{
    public DateTime Timestamp { get; init; }
    public DateTime CandleTimestamp { get; init; }
    public string Coin { get; init; } = string.Empty;
    public string PrimaryTimeframe { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Signal { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
}

public sealed class IndicatorSettings
{
    public bool SmaEnabled { get; set; } = true;

    public bool EmaEnabled { get; set; } = true;

    public bool RsiEnabled { get; set; } = true;

    public bool MacdEnabled { get; set; } = true;

    public bool AtrEnabled { get; set; } = true;

    public bool VolumeEnabled { get; set; } = true;

    public decimal MinimumRiskReward { get; set; } = 1.50m;

    public decimal SafetyBufferAtr { get; set; } = 0.10m;

    public decimal StrongSetupMinimumConfidence { get; set; } = 60m;

    public int MinimumMultiTimeframeAgreement { get; set; } = 2;

    public string GetSummary()
    {
        return
            $"SMA={(SmaEnabled ? "ON" : "OFF")} " +
            $"EMA={(EmaEnabled ? "ON" : "OFF")} " +
            $"RSI={(RsiEnabled ? "ON" : "OFF")} " +
            $"MACD={(MacdEnabled ? "ON" : "OFF")} " +
            $"ATR={(AtrEnabled ? "ON" : "OFF")} " +
            $"VOL={(VolumeEnabled ? "ON" : "OFF")}";
    }
}

public sealed class ApiStats
{
    public int TotalRequests { get; set; }

    public int ApiErrors { get; set; }

    public int ApiTimeouts { get; set; }
}

public enum MarketDirection
{
    Neutral,
    Bullish,
    Bearish
}

public sealed record Candle(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public sealed class TimeframeAnalysis
{
    public string Timeframe { get; init; } = string.Empty;

    public List<Candle> Candles { get; init; } = new();

    public DateTime Timestamp { get; init; }

    public decimal CurrentPrice { get; init; }

    public decimal Open { get; init; }

    public decimal High { get; init; }

    public decimal Low { get; init; }

    public decimal Close { get; init; }

    public decimal Volume { get; init; }

    public string Trend { get; init; } = string.Empty;

    public string Signal { get; init; } = string.Empty;

    public MarketDirection Direction { get; init; }

    public decimal? Sma { get; init; }

    public decimal? Ema { get; init; }

    public decimal? Rsi { get; init; }

    public decimal? Macd { get; init; }

    public decimal? MacdSignal { get; init; }

    public decimal? MacdHistogram { get; init; }

    public decimal? Atr { get; init; }

    public decimal Support { get; init; }

    public decimal Resistance { get; init; }

    public decimal AverageVolume { get; init; }

    public string VolumeStatus { get; init; } = string.Empty;

    public string PricePosition { get; init; } = string.Empty;

    public List<decimal> SupportLevels { get; init; } = new();

    public List<decimal> ResistanceLevels { get; init; } = new();

    public decimal SupportZoneLow { get; init; }

    public decimal SupportZoneHigh { get; init; }

    public decimal ResistanceZoneLow { get; init; }

    public decimal ResistanceZoneHigh { get; init; }

    public bool SupportTested { get; init; }

    public bool ResistanceTested { get; init; }

    public bool SupportRejection { get; init; }

    public bool ResistanceRejection { get; init; }

    public string LevelFlip { get; init; } = "NONE";

    public int StructuralAgreementCount { get; set; }

    public bool HigherTimeframeSupported { get; set; }

    public bool MultiTimeframeConflict { get; set; }

    public string HigherTimeframeStructure { get; set; } = "UNDEFINED";

    // MARKET STRUCTURE
    public decimal? LastSwingHigh { get; init; }

    public decimal? LastSwingLow { get; init; }

    public decimal? PreviousSwingHigh { get; init; }

    public decimal? PreviousSwingLow { get; init; }

    public string MarketStructure { get; init; } = "UNDEFINED";

    public string StructuralTrend { get; init; } = "UNDEFINED";

    public bool HigherHigh { get; init; }

    public bool HigherLow { get; init; }

    public bool LowerHigh { get; init; }

    public bool LowerLow { get; init; }

    public decimal BodySize { get; init; }

    public decimal TotalRange { get; init; }

    public decimal UpperWick { get; init; }

    public decimal LowerWick { get; init; }

    public decimal BodyRangeRatio { get; init; }

    public bool BullishCandle { get; init; }

    public bool BearishCandle { get; init; }

    public bool StrongCandle { get; init; }

    public bool WeakCandle { get; init; }

    public bool RejectionCandle { get; init; }

    public bool ContinuationCandle { get; init; }

    public string BreakoutCandleQuality { get; init; } = "NONE";

    public bool BullishBOS { get; init; }

    public bool BearishBOS { get; init; }

    public bool BullishCHoCH { get; init; }

    public bool BearishCHoCH { get; init; }

    public bool ResistanceBreakout { get; init; }

    public bool SupportBreakdown { get; init; }

    public bool CloseConfirmedBreakout { get; init; }

    public bool WickOnlyBreakout { get; init; }

    public string BreakoutStrength { get; init; } = "NONE";

    public bool BreakoutVolumeConfirmed { get; init; }

    public bool WeakBreakoutRejected { get; init; }

    public string RetestState { get; init; } = "NONE";

    public bool RetestConfirmed { get; init; }

    public bool RetestFailed { get; init; }

    public bool Invalidation { get; init; }

    public bool LongInvalidation { get; init; }

    public bool ShortInvalidation { get; init; }

    public string MarketRegime { get; init; } = "UNDEFINED";

    public bool StrongSetup { get; init; }

    public bool MomentumConfirmed { get; init; }

    public bool VolumeConfirmed { get; init; }

    public decimal StructureStopLoss { get; init; }

    public decimal StructureTarget1 { get; init; }

    public decimal StructureTarget2 { get; init; }

    public List<string> Reasons { get; init; } = new();

    public int TrendScore { get; set; }

    public int SmaEmaScore { get; set; }

    public int RsiScore { get; set; }

    public int MacdScore { get; set; }

    public int VolumeScore { get; set; }

    public int SupportResistanceScore { get; set; }

    public int MultiTimeframeScore { get; set; }

    public int MultiTimeframeAgreement { get; set; }

    public decimal Confidence { get; set; }
}

public sealed class PaperSetup
{
    public string Coin { get; init; } = string.Empty;

    public string Timeframe { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public decimal Entry { get; init; }

    public decimal StopLoss { get; init; }

    public decimal Target1 { get; init; }

    public decimal Target2 { get; init; }

    public decimal RiskRewardTarget1 { get; init; }

    public decimal RiskRewardTarget2 { get; init; }

    public decimal Confidence { get; init; }

    public DateTime Timestamp { get; init; }
}
  public sealed class PaperAccount
{
    public decimal StartingBalance { get; set; } = 10000000m;

    public decimal Balance { get; set; } = 10000000m;

    public decimal UsedBalance { get; set; } = 0m;

    public decimal RealizedProfit { get; set; } = 0m;

    public decimal RealizedLoss { get; set; } = 0m;

    public int TotalTrades { get; set; } = 0;

    public int WinningTrades { get; set; } = 0;

    public int LosingTrades { get; set; } = 0;

    public decimal PeakEquity { get; set; } = 10000000m;

    public decimal Drawdown { get; set; } = 0m;

    public decimal DrawdownPercent { get; set; } = 0m;

    public decimal TotalProfit =>
        RealizedProfit - RealizedLoss;

    public decimal Equity =>
        Balance + UsedBalance;

    public decimal WinRate
    {
        get
        {
            int resolved =
                WinningTrades + LosingTrades;

            return resolved > 0
                ? WinningTrades * 100m / resolved
                : 0m;
        }
    }

    public void OpenTrade(decimal amount)
    {
        if (amount <= 0)
            return;

        if (amount > Balance)
            amount = Balance;

        Balance -= amount;
        UsedBalance += amount;
        TotalTrades++;
        UpdateDrawdown();
    }

    public void CloseTrade(
        decimal investedAmount,
        decimal profitLoss,
        string status)
    {
        if (investedAmount < 0m)
            investedAmount = 0m;

        UsedBalance -= investedAmount;
        if (UsedBalance < 0m)
            UsedBalance = 0m;

        Balance += investedAmount + profitLoss;

        if (profitLoss > 0m)
            RealizedProfit += profitLoss;
        else if (profitLoss < 0m)
            RealizedLoss += Math.Abs(profitLoss);

        if (status == "WIN")
            WinningTrades++;
        else if (status == "LOSS")
            LosingTrades++;

        UpdateDrawdown();
    }

    public void ReleasePartialTrade(
        decimal investedAmount,
        decimal profitLoss)
    {
        if (investedAmount < 0m)
            investedAmount = 0m;

        UsedBalance -= investedAmount;
        if (UsedBalance < 0m)
            UsedBalance = 0m;

        Balance += investedAmount + profitLoss;

        if (profitLoss > 0m)
            RealizedProfit += profitLoss;
        else if (profitLoss < 0m)
            RealizedLoss += Math.Abs(profitLoss);

        UpdateDrawdown();
    }

    public void ReleaseAmbiguousTrade(decimal investedAmount)
    {
        if (investedAmount <= 0m)
            return;

        UsedBalance -= investedAmount;
        if (UsedBalance < 0m)
            UsedBalance = 0m;

        Balance += investedAmount;
        UpdateDrawdown();
    }

    public void UpdateDrawdown()
    {
        decimal equity = Equity;

        if (equity > PeakEquity)
            PeakEquity = equity;

        Drawdown = Math.Max(0m, PeakEquity - equity);
        DrawdownPercent = PeakEquity > 0m
            ? Drawdown * 100m / PeakEquity
            : 0m;
    }

    public void Print()
    {
        Console.WriteLine("------------------------------------------");
        Console.WriteLine("PAPER ACCOUNT");
        Console.WriteLine("------------------------------------------");

        Console.WriteLine(
            $"Starting Balance : {StartingBalance:F2}");

        Console.WriteLine(
            $"Balance          : {Balance:F2}");

        Console.WriteLine(
            $"Used Balance     : {UsedBalance:F2}");

        Console.WriteLine(
            $"Equity           : {Equity:F2}");

        Console.WriteLine(
            $"Profit           : {RealizedProfit:F2}");

        Console.WriteLine(
            $"Loss             : {RealizedLoss:F2}");

        Console.WriteLine(
            $"Net P/L          : {TotalProfit:F2}");

        Console.WriteLine(
            $"Total Trades     : {TotalTrades}");

        Console.WriteLine(
            $"Winning Trades   : {WinningTrades}");

        Console.WriteLine(
            $"Losing Trades    : {LosingTrades}");

        Console.WriteLine(
            $"Win Rate         : {WinRate:F2}%");

        Console.WriteLine(
            $"Peak Equity      : {PeakEquity:F2}");

        Console.WriteLine(
            $"Drawdown         : {Drawdown:F2}");

        Console.WriteLine(
            $"Drawdown %       : {DrawdownPercent:F2}%");
    }
}
public sealed class PaperTrade
{
    public decimal Units { get; set; }

   public int MonitoringMinutes { get; set; }

  public bool UserConfirmed { get; set; }
    public decimal InitialStopLoss { get; set; }

public decimal HighestPriceSinceEntry { get; set; }

public decimal LowestPriceSinceEntry { get; set; }

public decimal TrailingStopDistance { get; set; }

public bool TrailingStopActive { get; set; }

    public decimal InvestedAmount { get; set; }

    public decimal RemainingInvestedAmount { get; set; }

    public decimal RemainingUnits { get; set; }

    public bool Target1Hit { get; set; }

    public decimal RealizedPartialPnL { get; set; }

       public decimal SimulatedProfitLoss { get; set; }
       
    public string Coin { get; init; } = string.Empty;

    public string Timeframe { get; init; } = string.Empty;

    public string Direction { get; init; } = string.Empty;

    public decimal Entry { get; init; }

   public decimal StopLoss { get; set; }

    public decimal Target1 { get; init; }

    public decimal Target2 { get; init; }

    public decimal Confidence { get; init; }

    public DateTime Timestamp { get; init; }

    public DateTime EntryCandleTimestamp { get; init; }

    public string Status { get; set; } = "OPEN";

    public DateTime? ClosedTimestamp { get; set; }

    public decimal? OutcomePrice { get; set; }

    // Price-unit P/L because no position quantity was specified.
    public decimal SimulatedPnL { get; set; }

    public decimal PeakEquity { get; set; }

    public decimal Drawdown { get; set; }

    public decimal DrawdownPercent { get; set; }
}


internal sealed class NotificationService
{
    private readonly HttpClient _http;
    private readonly string? _telegramToken;
    private readonly string? _telegramChatId;
    private readonly string? _discordWebhookUrl;

    private NotificationService(
        HttpClient http,
        string? telegramToken,
        string? telegramChatId,
        string? discordWebhookUrl)
    {
        _http = http;
        _telegramToken = telegramToken;
        _telegramChatId = telegramChatId;
        _discordWebhookUrl = discordWebhookUrl;
    }

    public static NotificationService FromEnvironment(HttpClient http)
    {
        return new NotificationService(
            http,
            Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"),
            Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID"),
            Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL"));
    }

    public Task SendSignalAsync(
        string symbol,
        TimeframeAnalysis analysis)
    {
        string text =
            $"<b>TRADE SIGNAL</b>\n" +
            $"Symbol: <code>{Html(symbol)}</code>\n" +
            $"TF: <code>{Html(analysis.Timeframe)}</code>\n" +
            $"Signal: <b>{Html(analysis.Signal)}</b>\n" +
            $"Direction: <code>{Html(analysis.Direction.ToString())}</code>\n" +
            $"Confidence: <code>{analysis.Confidence:F0}/100</code>\n" +
            $"Price: <code>{analysis.CurrentPrice:F8}</code>\n" +
            $"Structure: <code>{Html(analysis.StructuralTrend)}</code>\n" +
            $"Regime: <code>{Html(analysis.MarketRegime)}</code>";

        return SendAsync(
            "TRADE SIGNAL",
            text,
            new Dictionary<string, string>
            {
                ["Symbol"] = symbol,
                ["Timeframe"] = analysis.Timeframe,
                ["Signal"] = analysis.Signal,
                ["Direction"] = analysis.Direction.ToString(),
                ["Confidence"] = $"{analysis.Confidence:F0}/100",
                ["Price"] = analysis.CurrentPrice.ToString("F8", CultureInfo.InvariantCulture),
                ["Structure"] = analysis.StructuralTrend,
                ["Regime"] = analysis.MarketRegime
            });
    }

    public Task SendSetupConfirmationAsync(PaperTrade trade)
    {
        string text =
            $"<b>SETUP CONFIRMED</b>\n" +
            $"Symbol: <code>{Html(trade.Coin)}</code>\n" +
            $"Direction: <b>{Html(trade.Direction)}</b>\n" +
            $"Entry: <code>{trade.Entry:F8}</code>\n" +
            $"Stop: <code>{trade.StopLoss:F8}</code>\n" +
            $"Target 1: <code>{trade.Target1:F8}</code>\n" +
            $"Target 2: <code>{trade.Target2:F8}</code>\n" +
            $"Units: <code>{trade.Units:F8}</code>\n" +
            $"Confidence: <code>{trade.Confidence:F0}/100</code>";

        return SendAsync(
            "SETUP CONFIRMED",
            text,
            new Dictionary<string, string>
            {
                ["Symbol"] = trade.Coin,
                ["Direction"] = trade.Direction,
                ["Entry"] = trade.Entry.ToString("F8", CultureInfo.InvariantCulture),
                ["Stop"] = trade.StopLoss.ToString("F8", CultureInfo.InvariantCulture),
                ["Target 1"] = trade.Target1.ToString("F8", CultureInfo.InvariantCulture),
                ["Target 2"] = trade.Target2.ToString("F8", CultureInfo.InvariantCulture),
                ["Units"] = trade.Units.ToString("F8", CultureInfo.InvariantCulture),
                ["Confidence"] = $"{trade.Confidence:F0}/100"
            });
    }

    public Task SendClosureAsync(
        PaperTrade trade,
        string status,
        decimal? exitPrice)
    {
        string exit =
            exitPrice.HasValue
                ? exitPrice.Value.ToString("F8", CultureInfo.InvariantCulture)
                : "N/A";

        string text =
            $"<b>TRADE CLOSED</b>\n" +
            $"Symbol: <code>{Html(trade.Coin)}</code>\n" +
            $"Status: <b>{Html(status)}</b>\n" +
            $"Direction: <code>{Html(trade.Direction)}</code>\n" +
            $"Entry: <code>{trade.Entry:F8}</code>\n" +
            $"Exit: <code>{exit}</code>\n" +
            $"P/L: <code>{trade.SimulatedPnL:F8}</code>";

        return SendAsync(
            "TRADE CLOSED",
            text,
            new Dictionary<string, string>
            {
                ["Symbol"] = trade.Coin,
                ["Status"] = status,
                ["Direction"] = trade.Direction,
                ["Entry"] = trade.Entry.ToString("F8", CultureInfo.InvariantCulture),
                ["Exit"] = exit,
                ["P/L"] = trade.SimulatedPnL.ToString("F8", CultureInfo.InvariantCulture)
            });
    }

    public Task SendTextAsync(string title, string message)
    {
        return SendAsync(
            title,
            $"<b>{Html(title)}</b>\n{Html(message)}",
            new Dictionary<string, string>
            {
                ["Message"] = message
            });
    }

    private async Task SendAsync(
        string title,
        string telegramHtml,
        IReadOnlyDictionary<string, string> fields)
    {
        var tasks = new List<Task>();

        if (!string.IsNullOrWhiteSpace(_telegramToken) &&
            !string.IsNullOrWhiteSpace(_telegramChatId))
        {
            tasks.Add(SendTelegramAsync(telegramHtml));
        }

        if (!string.IsNullOrWhiteSpace(_discordWebhookUrl))
        {
            tasks.Add(SendDiscordAsync(title, fields));
        }

        if (tasks.Count == 0)
            return;

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Notifications must never stop the scanner.
        }
    }

    private async Task SendTelegramAsync(string html)
    {
        string url =
            $"https://api.telegram.org/bot{_telegramToken}/sendMessage";

        var payload = new Dictionary<string, string>
        {
            ["chat_id"] = _telegramChatId!,
            ["text"] = html,
            ["parse_mode"] = "HTML"
        };

        using var content =
            new FormUrlEncodedContent(payload);

        using HttpResponseMessage response =
            await _http.PostAsync(url, content);

        response.EnsureSuccessStatusCode();
    }

    private async Task SendDiscordAsync(
        string title,
        IReadOnlyDictionary<string, string> fields)
    {
        var discordFields =
            fields.Select(
                pair => new
                {
                    name = pair.Key,
                    value = pair.Value,
                    inline = true
                })
            .ToArray();

        var payload = new
        {
            username = "Neon Futures Scanner",
            embeds = new[]
            {
                new
                {
                    title,
                    fields = discordFields,
                    timestamp = DateTime.UtcNow.ToString("O")
                }
            }
        };

        string json =
            JsonSerializer.Serialize(payload);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        using HttpResponseMessage response =
            await _http.PostAsync(
                _discordWebhookUrl!,
                content);

        response.EnsureSuccessStatusCode();
    }

    private static string Html(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}

internal sealed record BinanceOrderResult(
    long OrderId,
    string Status,
    string Symbol,
    string Side,
    string Type,
    decimal Price,
    decimal Quantity,
    bool IsLive);

internal sealed class FuturesBinanceClient
{
    private const string LiveBaseUrl =
        "https://fapi.binance.com";

    private const string TestnetBaseUrl =
        "https://testnet.binancefuture.com";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _baseUrl;

    public bool IsArmed { get; }
    public bool IsLive { get; }

    private FuturesBinanceClient(
        HttpClient http,
        string apiKey,
        string apiSecret,
        string baseUrl,
        bool isArmed,
        bool isLive)
    {
        _http = http;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _baseUrl = baseUrl;
        IsArmed = isArmed;
        IsLive = isLive;
    }

    public static FuturesBinanceClient? FromEnvironment(HttpClient http)
    {
        bool liveEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "NEON_LIVE_TRADING_ENABLED"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        bool liveKillSwitchArmed =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "NEON_LIVE_KILL_SWITCH"),
                "ARMED",
                StringComparison.Ordinal);

        bool liveConfirmation =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "NEON_LIVE_CONFIRM"),
                "I_UNDERSTAND_LIVE_TRADING",
                StringComparison.Ordinal);

        if (liveEnabled && liveKillSwitchArmed && liveConfirmation)
        {
            string? apiKey =
                Environment.GetEnvironmentVariable(
                    "BINANCE_FUTURES_API_KEY");

            string? apiSecret =
                Environment.GetEnvironmentVariable(
                    "BINANCE_FUTURES_API_SECRET");

            if (!string.IsNullOrWhiteSpace(apiKey) &&
                !string.IsNullOrWhiteSpace(apiSecret))
            {
                return new FuturesBinanceClient(
                    http,
                    apiKey,
                    apiSecret,
                    LiveBaseUrl,
                    true,
                    true);
            }
        }

        bool testnetEnabled =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "NEON_TESTNET_ORDER_ENABLED"),
                "true",
                StringComparison.OrdinalIgnoreCase);

        bool testnetKillSwitchArmed =
            string.Equals(
                Environment.GetEnvironmentVariable(
                    "NEON_TESTNET_KILL_SWITCH"),
                "ARMED",
                StringComparison.Ordinal);

        if (testnetEnabled && testnetKillSwitchArmed)
        {
            string? apiKey =
                Environment.GetEnvironmentVariable(
                    "BINANCE_FUTURES_TESTNET_API_KEY");

            string? apiSecret =
                Environment.GetEnvironmentVariable(
                    "BINANCE_FUTURES_TESTNET_API_SECRET");

            if (!string.IsNullOrWhiteSpace(apiKey) &&
                !string.IsNullOrWhiteSpace(apiSecret))
            {
                return new FuturesBinanceClient(
                    http,
                    apiKey,
                    apiSecret,
                    TestnetBaseUrl,
                    true,
                    false);
            }
        }

        return null;
    }

    public async Task<BinanceOrderResult> PlaceLimitOrderAsync(
        string symbol,
        string side,
        decimal quantity,
        decimal price)
    {
        if (!IsArmed)
            throw new InvalidOperationException(
                "Binance Futures order client is disarmed by the kill-switch.");

        if (quantity <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(quantity));

        if (price <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(price));

        var parameters =
            new Dictionary<string, string>
            {
                ["symbol"] = symbol.ToUpperInvariant(),
                ["side"] = side.ToUpperInvariant(),
                ["type"] = "LIMIT",
                ["timeInForce"] = "GTC",
                ["quantity"] = quantity.ToString("0.########", CultureInfo.InvariantCulture),
                ["price"] = price.ToString("0.########", CultureInfo.InvariantCulture),
                ["recvWindow"] = "5000",
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            };

        string query =
            string.Join(
                "&",
                parameters.Select(
                    pair =>
                        $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        string signature =
            CreateSignature(query, _apiSecret);

        string requestUri =
            $"{_baseUrl}/fapi/v1/order?{query}&signature={signature}";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                requestUri);

        request.Headers.TryAddWithoutValidation(
            "X-MBX-APIKEY",
            _apiKey);

        using HttpResponseMessage response =
            await _http.SendAsync(request);

        string body =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Binance Futures {(IsLive ? "LIVE" : "TESTNET")} order failed ({(int)response.StatusCode}): {body}");
        }

        using JsonDocument document =
            JsonDocument.Parse(body);

        JsonElement root = document.RootElement;

        return new BinanceOrderResult(
            root.GetProperty("orderId").GetInt64(),
            root.GetProperty("status").GetString() ?? string.Empty,
            root.GetProperty("symbol").GetString() ?? string.Empty,
            root.GetProperty("side").GetString() ?? string.Empty,
            root.GetProperty("type").GetString() ?? string.Empty,
            decimal.Parse(
                root.GetProperty("price").GetString() ?? "0",
                CultureInfo.InvariantCulture),
            decimal.Parse(
                root.GetProperty("origQty").GetString() ?? "0",
                CultureInfo.InvariantCulture),
            IsLive);
    }

    private static string CreateSignature(
        string queryString,
        string secret)
    {
        using var hmac =
            new HMACSHA256(
                Encoding.UTF8.GetBytes(secret));

        byte[] hash =
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes(queryString));

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }
}
