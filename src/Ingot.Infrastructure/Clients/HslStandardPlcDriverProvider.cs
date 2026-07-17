using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using HslCommunication.Core.Device;
using HslCommunication.Profinet.AllenBradley;
using HslCommunication.Profinet.Beckhoff;
using HslCommunication.Profinet.Delta;
using HslCommunication.Profinet.FATEK;
using HslCommunication.Profinet.Freedom;
using HslCommunication.Profinet.Fuji;
using HslCommunication.Profinet.GE;
using HslCommunication.Profinet.Inovance;
using HslCommunication.Profinet.Keyence;
using HslCommunication.Profinet.LSIS;
using HslCommunication.Profinet.MegMeet;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Omron;
using HslCommunication.Profinet.Panasonic;
using HslCommunication.Profinet.Siemens;
using HslCommunication.Profinet.Toyota;
using HslCommunication.Profinet.Turck;
using HslCommunication.Profinet.Vigor;
using HslCommunication.Profinet.XINJE;
using HslCommunication.Profinet.Yamatake;
using HslCommunication.Profinet.YASKAWA;
using HslCommunication.Profinet.Yokogawa;

namespace Ingot.Infrastructure.Clients;

/// <summary>
///     标准 Hsl 驱动提供者。显式声明默认支持的 Hsl PLC 驱动，并通过统一 HslPlcClientService 暴露给采集框架。
/// </summary>
public sealed class HslStandardPlcDriverProvider : IPlcDriverProvider
{
    private const int DefaultConnectTimeoutMs = 5000;
    private const int DefaultReceiveTimeoutMs = 5000;

    private static class ProtocolOptionNames
    {
        public const string ConnectTimeoutMs = "connect-timeout-ms";
        public const string ReceiveTimeoutMs = "receive-timeout-ms";
        public const string Station = "station";
        public const string Series = "series";
        public const string Plc = "plc";
        public const string CpuType = "cpu-type";
        public const string SlotNo = "slot-no";
    }

    private static readonly string[] CommonProtocolOptionNames =
        [ProtocolOptionNames.ConnectTimeoutMs, ProtocolOptionNames.ReceiveTimeoutMs];

    private static readonly IReadOnlyList<HslDriverDefinition> DriverDefinitions =
    [
        new("allen-bradley-connected-cip", [], CreateAllenBradleyConnectedCip),
        new("allen-bradley-micro-cip", [], CreateAllenBradleyMicroCip),
        new("allen-bradley-net", [], CreateAllenBradleyNet),
        new("allen-bradley-pccc", [], CreateAllenBradleyPccc),
        new("allen-bradley-slc", [], CreateAllenBradleySlc),
        new("beckhoff-ads", [], CreateBeckhoffAds),
        new("delta-serial-ascii-over-tcp", [ProtocolOptionNames.Station], CreateDeltaSerialAsciiOverTcp),
        new("delta-serial-over-tcp", [ProtocolOptionNames.Station], CreateDeltaSerialOverTcp),
        new("delta-tcp", [ProtocolOptionNames.Station], CreateDeltaTcp),
        new("fatek-program", [], CreateFatekProgram),
        new("freedom-tcp", [], CreateFreedomTcp),
        new("freedom-udp", [], CreateFreedomUdp),
        new("fuji-command-setting-type", [], CreateFujiCommandSettingType),
        new("fuji-spb", [], CreateFujiSpb),
        new("fuji-sph", [], CreateFujiSph),
        new("ge-srtp", [], CreateGeSrtp),
        new("inovance-serial-over-tcp", [ProtocolOptionNames.Series, ProtocolOptionNames.Station], CreateInovanceSerialOverTcp),
        new("inovance-tcp", [ProtocolOptionNames.Series, ProtocolOptionNames.Station], CreateInovanceTcp),
        new("keyence-mc-ascii", [], CreateKeyenceMcAscii),
        new("keyence-mc", [], CreateKeyenceMc),
        new("keyence-nano-serial-over-tcp", [], CreateKeyenceNanoSerialOverTcp),
        new("lsis-cnet-over-tcp", [], CreateLsCnetOverTcp),
        new("lsis-fast-enet", [ProtocolOptionNames.CpuType, ProtocolOptionNames.SlotNo], CreateLsFastEnet),
        new("megmeet-serial-over-tcp", [ProtocolOptionNames.Station], CreateMegMeetSerialOverTcp),
        new("megmeet-tcp", [ProtocolOptionNames.Station], CreateMegMeetTcp),
        new("melsec-a1e-ascii", [], CreateMelsecA1EAscii),
        new("melsec-a1e", [], CreateMelsecA1E),
        new("melsec-a3c", [], CreateMelsecA3C),
        new("melsec-cip", [], CreateMelsecCip),
        new("melsec-fxlinks", [], CreateMelsecFxLinks),
        new("melsec-fxserial", [], CreateMelsecFxSerial),
        new("melsec-mc-ascii", [], CreateMelsecMcAscii),
        new("melsec-mc-ascii-udp", [], CreateMelsecMcAsciiUdp),
        new("melsec-mc", [], CreateMelsecMc),
        new("melsec-mcr", [], CreateMelsecMcR),
        new("melsec-mc-udp", [], CreateMelsecMcUdp),
        new("omron-cip", [], CreateOmronCip),
        new("omron-connected-cip", [], CreateOmronConnectedCip),
        new("omron-fins", [], CreateOmronFins),
        new("omron-hostlink-cmode", [], CreateOmronHostLinkCMode),
        new("omron-hostlink", [], CreateOmronHostLink),
        new("panasonic-mc", [], CreatePanasonicMc),
        new("panasonic-mewtocol", [ProtocolOptionNames.Station], CreatePanasonicMewtocol),
        new("siemens-fetch-write", [], CreateSiemensFetchWrite),
        new("siemens-ppi-over-tcp", [], CreateSiemensPpiOverTcp),
        new("siemens-s7", [ProtocolOptionNames.Plc], CreateSiemensS7),
        new("toyota-toyopuc", [], CreateToyotaToyoPuc),
        new("turck-reader", [], CreateTurckReader),
        new("vigor-serial-over-tcp", [], CreateVigorSerialOverTcp),
        new("xinje-internal", [ProtocolOptionNames.Station], CreateXinjeInternal),
        new("xinje-serial-over-tcp", [ProtocolOptionNames.Series, ProtocolOptionNames.Station], CreateXinjeSerialOverTcp),
        new("xinje-tcp", [ProtocolOptionNames.Series, ProtocolOptionNames.Station], CreateXinjeTcp),
        new("yamatake-digitron-cpl-over-tcp", [], CreateYamatakeDigitronCplOverTcp),
        new("yaskawa-memobus-tcp", [], CreateYaskawaMemobusTcp),
        new("yaskawa-memobus-udp", [], CreateYaskawaMemobusUdp),
        new("yokogawa-link", [], CreateYokogawaLink)
    ];

    private static readonly IReadOnlyDictionary<string, HslDriverDefinition> DriverDefinitionsByName =
        DriverDefinitions.ToDictionary(static definition => NormalizeDriverName(definition.Driver),
            static definition => definition,
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyCollection<string> RegisteredDrivers =
        DriverDefinitions.Select(static definition => definition.Driver).ToArray();

    public IReadOnlyCollection<string> SupportedDrivers => RegisteredDrivers;

    public IPlcClientService Create(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var definition = ResolveDefinition(config);

        ValidateNetworkEndpoint(config, definition.Driver);
        ValidateProtocolOptions(config, definition);
        var device = definition.Factory(config);
        ApplyCommonSettings(device, config);
        return new HslPlcClientService(device);
    }

    private static HslDriverDefinition ResolveDefinition(DeviceConfig config)
    {
        var driver = NormalizeDriverName(config.Driver ?? string.Empty);
        if (DriverDefinitionsByName.TryGetValue(driver, out var definition))
            return definition;

        throw new NotSupportedException(
            $"未找到匹配的 Hsl PLC 驱动。SourceCode={config.SourceCode}, Driver={config.Driver}");
    }

    private static string NormalizeDriverName(string value) => value.Trim().ToLowerInvariant();

    private static string NormalizeOptionName(string value)
    {
        var normalized = value.Trim().Replace('_', '-').Replace(':', '-').ToLowerInvariant();
        return normalized switch
        {
            "connecttimeoutms" => ProtocolOptionNames.ConnectTimeoutMs,
            "receivetimeoutms" => ProtocolOptionNames.ReceiveTimeoutMs,
            "cputype" => ProtocolOptionNames.CpuType,
            "slotno" => ProtocolOptionNames.SlotNo,
            _ => normalized
        };
    }

    private static void ApplyCommonSettings(DeviceTcpNet device, DeviceConfig config)
    {
        device.IpAddress = config.Host.Trim();
        device.Port = config.Port;
        device.ConnectTimeOut = GetIntOption(config, ProtocolOptionNames.ConnectTimeoutMs, DefaultConnectTimeoutMs);
        device.ReceiveTimeOut = GetIntOption(config, ProtocolOptionNames.ReceiveTimeoutMs, DefaultReceiveTimeoutMs);
    }

    private static void ValidateNetworkEndpoint(DeviceConfig config, string driver)
    {
        if (string.IsNullOrWhiteSpace(config.Host))
            throw new InvalidOperationException($"驱动 {driver} 缺少 Host 配置。");

        if (config.Port == 0)
            throw new InvalidOperationException($"驱动 {driver} 缺少有效的 Port 配置。");
    }

    private static int GetIntOption(DeviceConfig config, string key, int defaultValue)
    {
        if (TryGetOption(config, key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return defaultValue;
    }

    private static byte GetByteOption(DeviceConfig config, string key, byte defaultValue = 0)
    {
        if (TryGetOption(config, key, out var raw) &&
            byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        return defaultValue;
    }

    private static string GetStringOption(DeviceConfig config, string key, string defaultValue = "")
    {
        return TryGetOption(config, key, out var raw) ? raw : defaultValue;
    }

    private static bool TryGetOption(DeviceConfig config, string key, out string value)
    {
        foreach (var item in config.ProtocolOptions)
        {
            if (NormalizeOptionName(item.Key) == NormalizeOptionName(key))
            {
                value = item.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void ValidateProtocolOptions(DeviceConfig config, HslDriverDefinition definition)
    {
        if (config.ProtocolOptions.Count == 0)
            return;

        var allowed = definition.SupportedProtocolOptions
            .Concat(CommonProtocolOptionNames)
            .Select(NormalizeOptionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unsupported = config.ProtocolOptions.Keys
            .Select(static key => key.Trim())
            .Where(key => !allowed.Contains(NormalizeOptionName(key)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (unsupported.Length == 0)
            return;

        throw new NotSupportedException(
            $"驱动 {definition.Driver} 不支持以下 ProtocolOptions: {string.Join(", ", unsupported)}");
    }

    private static SiemensPLCS GetSiemensPlc(DeviceConfig config)
    {
        var raw = GetStringOption(config, ProtocolOptionNames.Plc, "S1200");
        return Enum.Parse<SiemensPLCS>(raw, ignoreCase: true);
    }

    private static InovanceSeries GetInovanceSeries(DeviceConfig config)
    {
        var raw = GetStringOption(config, ProtocolOptionNames.Series, "AM");
        return Enum.Parse<InovanceSeries>(raw, ignoreCase: true);
    }

    private static XinJESeries GetXinjeSeries(DeviceConfig config)
    {
        var raw = GetStringOption(config, ProtocolOptionNames.Series, "XC");
        return Enum.Parse<XinJESeries>(raw, ignoreCase: true);
    }

    private static DeviceTcpNet CreateAllenBradleyConnectedCip(DeviceConfig config) => new AllenBradleyConnectedCipNet(config.Host, config.Port);
    private static DeviceTcpNet CreateAllenBradleyMicroCip(DeviceConfig config) => new AllenBradleyMicroCip(config.Host, config.Port);
    private static DeviceTcpNet CreateAllenBradleyNet(DeviceConfig config) => new AllenBradleyNet(config.Host, config.Port);
    private static DeviceTcpNet CreateAllenBradleyPccc(DeviceConfig config) => new AllenBradleyPcccNet(config.Host, config.Port);
    private static DeviceTcpNet CreateAllenBradleySlc(DeviceConfig config) => new AllenBradleySLCNet(config.Host, config.Port);
    private static DeviceTcpNet CreateBeckhoffAds(DeviceConfig config) => new BeckhoffAdsNet(config.Host, config.Port);
    private static DeviceTcpNet CreateDeltaSerialAsciiOverTcp(DeviceConfig config) => new DeltaSerialAsciiOverTcp(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateDeltaSerialOverTcp(DeviceConfig config) => new DeltaSerialOverTcp(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateDeltaTcp(DeviceConfig config) => new DeltaTcpNet(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateFatekProgram(DeviceConfig config) => new FatekProgramOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateFreedomTcp(DeviceConfig config) => new FreedomTcpNet(config.Host, config.Port);
    private static DeviceTcpNet CreateFreedomUdp(DeviceConfig config) => new FreedomUdpNet(config.Host, config.Port);
    private static DeviceTcpNet CreateFujiCommandSettingType(DeviceConfig config) => new FujiCommandSettingType(config.Host, config.Port);
    private static DeviceTcpNet CreateFujiSpb(DeviceConfig config) => new FujiSPBOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateFujiSph(DeviceConfig config) => new FujiSPHNet(config.Host, config.Port);
    private static DeviceTcpNet CreateGeSrtp(DeviceConfig config) => new GeSRTPNet(config.Host, config.Port);
    private static DeviceTcpNet CreateInovanceSerialOverTcp(DeviceConfig config) => new InovanceSerialOverTcp(GetInovanceSeries(config), config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateInovanceTcp(DeviceConfig config) => new InovanceTcpNet(GetInovanceSeries(config), config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateKeyenceMcAscii(DeviceConfig config) => new KeyenceMcAsciiNet(config.Host, config.Port);
    private static DeviceTcpNet CreateKeyenceMc(DeviceConfig config) => new KeyenceMcNet(config.Host, config.Port);
    private static DeviceTcpNet CreateKeyenceNanoSerialOverTcp(DeviceConfig config) => new KeyenceNanoSerialOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateLsCnetOverTcp(DeviceConfig config) => new LSCnetOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateLsFastEnet(DeviceConfig config)
    {
        if (TryGetOption(config, ProtocolOptionNames.CpuType, out _) || TryGetOption(config, ProtocolOptionNames.SlotNo, out _))
            return new LSFastEnet(
                GetStringOption(config, ProtocolOptionNames.CpuType),
                config.Host,
                config.Port,
                GetByteOption(config, ProtocolOptionNames.SlotNo));
        return new LSFastEnet(config.Host, config.Port);
    }
    private static DeviceTcpNet CreateMegMeetSerialOverTcp(DeviceConfig config) => new MegMeetSerialOverTcp(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateMegMeetTcp(DeviceConfig config) => new MegMeetTcpNet(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateMelsecA1EAscii(DeviceConfig config) => new MelsecA1EAsciiNet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecA1E(DeviceConfig config) => new MelsecA1ENet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecA3C(DeviceConfig config) => new MelsecA3CNetOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecCip(DeviceConfig config) => new MelsecCipNet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecFxLinks(DeviceConfig config) => new MelsecFxLinksOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecFxSerial(DeviceConfig config) => new MelsecFxSerialOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecMcAscii(DeviceConfig config) => new MelsecMcAsciiNet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecMcAsciiUdp(DeviceConfig config) => new MelsecMcAsciiUdp(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecMc(DeviceConfig config) => new MelsecMcNet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecMcR(DeviceConfig config) => new MelsecMcRNet(config.Host, config.Port);
    private static DeviceTcpNet CreateMelsecMcUdp(DeviceConfig config) => new MelsecMcUdp(config.Host, config.Port);
    private static DeviceTcpNet CreateOmronCip(DeviceConfig config) => new OmronCipNet(config.Host, config.Port);
    private static DeviceTcpNet CreateOmronConnectedCip(DeviceConfig config) => new OmronConnectedCipNet(config.Host, config.Port);
    private static DeviceTcpNet CreateOmronFins(DeviceConfig config) => new OmronFinsNet(config.Host, config.Port);
    private static DeviceTcpNet CreateOmronHostLinkCMode(DeviceConfig config) => new OmronHostLinkCModeOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateOmronHostLink(DeviceConfig config) => new OmronHostLinkOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreatePanasonicMc(DeviceConfig config) => new PanasonicMcNet(config.Host, config.Port);
    private static DeviceTcpNet CreatePanasonicMewtocol(DeviceConfig config) => new PanasonicMewtocolOverTcp(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateSiemensFetchWrite(DeviceConfig config) => new SiemensFetchWriteNet(config.Host, config.Port);
    private static DeviceTcpNet CreateSiemensPpiOverTcp(DeviceConfig config) => new SiemensPPIOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateSiemensS7(DeviceConfig config) => new SiemensS7Net(GetSiemensPlc(config), config.Host);
    private static DeviceTcpNet CreateToyotaToyoPuc(DeviceConfig config) => new ToyoPuc(config.Host, config.Port);
    private static DeviceTcpNet CreateTurckReader(DeviceConfig config) => new ReaderNet(config.Host, config.Port);
    private static DeviceTcpNet CreateVigorSerialOverTcp(DeviceConfig config) => new VigorSerialOverTcp(config.Host, config.Port);
    private static DeviceTcpNet CreateXinjeInternal(DeviceConfig config) => new XinJEInternalNet(config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateXinjeSerialOverTcp(DeviceConfig config) => new XinJESerialOverTcp(GetXinjeSeries(config), config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateXinjeTcp(DeviceConfig config) => new XinJETcpNet(GetXinjeSeries(config), config.Host, config.Port, GetByteOption(config, ProtocolOptionNames.Station));
    private static DeviceTcpNet CreateYamatakeDigitronCplOverTcp(DeviceConfig config) => new DigitronCPLOverTcp();
    private static DeviceTcpNet CreateYaskawaMemobusTcp(DeviceConfig config) => new MemobusTcpNet(config.Host, config.Port);
    private static DeviceTcpNet CreateYaskawaMemobusUdp(DeviceConfig config) => new MemobusUdpNet(config.Host, config.Port);
    private static DeviceTcpNet CreateYokogawaLink(DeviceConfig config) => new YokogawaLinkTcp(config.Host, config.Port);

    private sealed record HslDriverDefinition(
        string Driver,
        IReadOnlyCollection<string> SupportedProtocolOptions,
        Func<DeviceConfig, DeviceTcpNet> Factory);
}
