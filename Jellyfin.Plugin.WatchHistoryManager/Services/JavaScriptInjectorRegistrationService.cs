using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.WatchHistoryManager.Services;

/// <summary>
/// Registers the Watch History Manager frontend script with the JavaScript Injector plugin, if available.
/// </summary>
internal sealed class JavaScriptInjectorRegistrationService : IHostedService
{
    private const string ScriptFileName = "startpoint-button.js";
    private readonly ILogger<JavaScriptInjectorRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JavaScriptInjectorRegistrationService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public JavaScriptInjectorRegistrationService(ILogger<JavaScriptInjectorRegistrationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;

            if (config is null || !config.EnableStartPointButton)
            {
                _logger.LogInformation("Start point button is disabled. JavaScript registration skipped.");
                TryUnregisterScript();
                return Task.CompletedTask;
            }

            var script = LoadScript();

            if (string.IsNullOrWhiteSpace(script))
            {
                _logger.LogWarning("Start point button script is empty or missing.");
                return Task.CompletedTask;
            }

            RegisterScript(script);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Watch History Manager JavaScript.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        TryUnregisterScript();
        return Task.CompletedTask;
    }

    private static string GetScriptId()
    {
        return $"{Plugin.Instance?.Id}-startpoint-button";
    }

    private static string GetPluginId()
    {
        return Plugin.Instance?.Id.ToString() ?? "watch-history-manager";
    }

    private string LoadScript()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var pluginDirectory = Path.GetDirectoryName(assemblyLocation);

        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            _logger.LogWarning("Could not determine plugin directory.");
            return string.Empty;
        }

        var scriptPath = Path.Combine(pluginDirectory, "Web", ScriptFileName);

        if (!File.Exists(scriptPath))
        {
            _logger.LogWarning("Start point button script was not found at {ScriptPath}.", scriptPath);
            return string.Empty;
        }

        return File.ReadAllText(scriptPath);
    }

    private void RegisterScript(string script)
    {
        var pluginInterfaceType = GetJavaScriptInjectorPluginInterfaceType();

        if (pluginInterfaceType is null)
        {
            _logger.LogWarning(
                "JavaScript Injector plugin was not found. Install JavaScript Injector to enable the start point button.");

            return;
        }

        var registration = new JObject
        {
            { "id", GetScriptId() },
            { "name", "Watch History Manager - Startpunkt setzen" },
            { "script", script },
            { "enabled", true },
            { "requiresAuthentication", true },
            { "pluginId", GetPluginId() },
            { "pluginName", Plugin.Instance?.Name ?? "Watch History Manager" },
            { "pluginVersion", Plugin.Instance?.Version.ToString() ?? "0.0.0.0" }
        };

        var result = pluginInterfaceType
            .GetMethod("RegisterScript")
            ?.Invoke(null, new object[] { registration });

        if (result is bool success && success)
        {
            _logger.LogInformation("Registered Watch History Manager JavaScript with JavaScript Injector.");
            return;
        }

        _logger.LogWarning("JavaScript Injector did not accept the Watch History Manager script registration.");
    }

    private void TryUnregisterScript()
    {
        try
        {
            var pluginInterfaceType = GetJavaScriptInjectorPluginInterfaceType();

            if (pluginInterfaceType is null)
            {
                return;
            }

            pluginInterfaceType
                .GetMethod("UnregisterScript")
                ?.Invoke(null, new object[] { GetScriptId() });

            _logger.LogInformation("Unregistered Watch History Manager JavaScript from JavaScript Injector.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not unregister Watch History Manager JavaScript.");
        }
    }

    private static Type? GetJavaScriptInjectorPluginInterfaceType()
    {
        var jsInjectorAssembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(assembly =>
                assembly.FullName?.Contains("Jellyfin.Plugin.JavaScriptInjector", StringComparison.OrdinalIgnoreCase) == true);

        return jsInjectorAssembly?.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
    }
}
