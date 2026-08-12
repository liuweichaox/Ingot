using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

/// <summary>
///     点位选择器语法在平台保存、Edge 应用和设备探查阶段保持一致。
/// </summary>
public class AcquisitionSelectorTests
{
    [Theory]
    [InlineData("D:100:int16", "D", 100u, 1)]
    [InlineData("D:100:float32", "D", 100u, 2)]
    [InlineData("D:100:string:16", "D", 100u, 8)]
    [InlineData("R:0:uint16", "R", 0u, 1)]
    public void Melsec_ParsesWordDevices(string selector, string device, uint address, int words)
    {
        Assert.True(AcquisitionSelectors.TryParseMelsec(selector, out var point, out var error), error);
        Assert.Equal(device, point!.Device.Code);
        Assert.Equal(address, point.WireAddress);
        Assert.Equal(words, point.WordCount);
        Assert.False(point.UsesBitRead);
    }

    [Fact]
    public void Melsec_TranslatesOctalInputRelayNumbersToWireAddresses()
    {
        // FX 系列 X/Y 按八进制编号：X17 是第 16 个输入点，线缆编号为 15。
        Assert.True(AcquisitionSelectors.TryParseMelsec("X:17:boolean", out var point, out var error), error);
        Assert.Equal(15u, point!.WireAddress);
        Assert.Equal("17", point.DisplayAddress);
        Assert.True(point.UsesBitRead);
    }

    [Fact]
    public void Melsec_RejectsOctalDigitsOutsideRange()
    {
        Assert.False(AcquisitionSelectors.TryParseMelsec("X:18:boolean", out _, out var error));
        Assert.Contains("八进制", error);
    }

    [Fact]
    public void Melsec_TranslatesHexadecimalLinkRelayNumbers()
    {
        Assert.True(AcquisitionSelectors.TryParseMelsec("B:1A:boolean", out var point, out var error), error);
        Assert.Equal(26u, point!.WireAddress);
    }

    [Fact]
    public void Melsec_WordDeviceBooleanRequiresBitOffset()
    {
        Assert.False(AcquisitionSelectors.TryParseMelsec("D:100:boolean", out _, out var error));
        Assert.Contains("位偏移", error);

        Assert.True(AcquisitionSelectors.TryParseMelsec("D:100.3:boolean", out var point, out var ok), ok);
        Assert.Equal(3, point!.BitIndex);
        Assert.False(point.UsesBitRead);
    }

    [Fact]
    public void Melsec_BitDeviceRejectsRedundantBitOffset()
    {
        Assert.False(AcquisitionSelectors.TryParseMelsec("M:10.2:boolean", out _, out var error));
        Assert.Contains("位软元件", error);
    }

    [Theory]
    [InlineData("Q:100:int16")]
    [InlineData("D:100")]
    [InlineData("D:abc:int16")]
    [InlineData("D:100:decimal")]
    [InlineData("D:100:string")]
    public void Melsec_RejectsMalformedSelectors(string selector)
        => Assert.False(AcquisitionSelectors.TryParseMelsec(selector, out _, out _));

    [Fact]
    public void RegisterSelectorsRejectReadsThatCrossTheAddressBoundary()
    {
        Assert.False(AcquisitionSelectors.TryParseModbus(
            "holding-register:65535:float32:big-endian:high-low", out _, out var modbusError));
        Assert.Contains("地址边界", modbusError);

        Assert.False(AcquisitionSelectors.TryParseMelsec(
            "D:4294967295:float32", out _, out var melsecError));
        Assert.Contains("地址边界", melsecError);
    }

    [Fact]
    public void Modbus_BitAreaOnlyAcceptsBoolean()
    {
        Assert.True(AcquisitionSelectors.TryParseModbus("coil:12:boolean", out var point, out var error), error);
        Assert.Equal(12, point!.Address);
        Assert.Null(point.BitIndex);

        Assert.False(AcquisitionSelectors.TryParseModbus("coil:12:int16", out _, out var typeError));
        Assert.Contains("位区", typeError);
    }

    [Fact]
    public void Modbus_RegisterBooleanRequiresBitOffset()
    {
        Assert.False(AcquisitionSelectors.TryParseModbus("holding-register:100:boolean", out _, out var error));
        Assert.Contains("位偏移", error);

        Assert.True(AcquisitionSelectors.TryParseModbus("holding-register:100.7:boolean", out var point, out var ok), ok);
        Assert.Equal(7, point!.BitIndex);
        Assert.Equal(1, point.Quantity);
    }

    [Fact]
    public void Modbus_ParsesByteAndWordOrderSegments()
    {
        Assert.True(AcquisitionSelectors.TryParseModbus(
            "holding-register:40:float32:little-endian:low-high", out var point, out var error), error);
        Assert.Equal("little-endian", point!.ByteOrder);
        Assert.Equal("low-high", point.WordOrder);
        Assert.Equal(2, point.Quantity);
    }

    [Fact]
    public void Modbus_StringSegmentIsByteLengthNotByteOrder()
    {
        Assert.True(AcquisitionSelectors.TryParseModbus("holding-register:40:string:16", out var point, out var error), error);
        Assert.Equal(8, point!.Quantity);
        Assert.Equal((ushort)16, point.ByteLength);
        Assert.Equal("big-endian", point.ByteOrder);
    }

    [Fact]
    public void RegisterStringSelectors_PreserveOddByteLengthsAcrossStructuredRoundTrip()
    {
        Assert.True(AcquisitionSelectors.TryParseModbus(
            "holding-register:40:string:3", out var modbus, out var modbusError), modbusError);
        var modbusMapping = new AcquisitionValueMapping
        {
            DataItemCode = "part.code",
            SourcePath = "holding-register:40:string:3",
            SourceDataType = modbus!.DataType,
            SourceByteLength = modbus.ByteLength,
            ModbusArea = modbus.Area,
            ModbusAddress = modbus.Address,
            ModbusQuantity = modbus.Quantity
        };
        Assert.Equal("holding-register:40:string:3", AcquisitionSelectors.FormatModbus(modbusMapping));

        Assert.True(AcquisitionSelectors.TryParseMelsec(
            "D:40:string:3", out var melsec, out var melsecError), melsecError);
        var melsecMapping = modbusMapping with
        {
            SourcePath = "D:40:string:3",
            MelsecDevice = melsec!.Device.Code,
            MelsecAddress = melsec.DisplayAddress,
            ModbusQuantity = (ushort)melsec.WordCount,
            SourceByteLength = melsec.ByteLength
        };
        Assert.Equal("D:40:string:3", AcquisitionSelectors.FormatMelsec(melsecMapping));
    }

    [Fact]
    public void RegisterStringDecoders_DoNotConsumePaddingBeyondDeclaredOddLength()
    {
        var modbusValue = ModbusTcpAcquisitionRunner.Decode([0x4142, 0x4358], new AcquisitionValueMapping
        {
            DataItemCode = "part.code",
            SourcePath = "holding-register:40:string:3",
            SourceDataType = "string",
            SourceByteLength = 3,
            ModbusArea = "holding-register",
            ModbusAddress = 40,
            ModbusQuantity = 2
        });
        Assert.Equal("ABC", modbusValue);

        Assert.True(AcquisitionSelectors.TryParseMelsec("D:40:string:3", out var point, out var error), error);
        var melsecValue = MelsecA1EAcquisitionRunner.Decode([0x81, 0x00, 0x41, 0x42, 0x43, 0x58], point!);
        Assert.Equal("ABC", melsecValue);
    }

    [Fact]
    public void Melsec_ReadPlanMergesNeighbouringPointsIntoOneRequest()
    {
        var selectors = new[] { "D:100:int16", "D:101:int16", "D:104:float32" }
            .ToDictionary(
                selector => selector,
                selector =>
                {
                    AcquisitionSelectors.TryParseMelsec(selector, out var point, out _);
                    return point!;
                },
                StringComparer.Ordinal);

        var merged = MelsecA1EAcquisitionRunner.BuildReadPlan(selectors, maxMergeGap: 8);
        Assert.Single(merged);
        Assert.Equal(100u, merged[0].Start);
        Assert.Equal(6, merged[0].Count);
        Assert.Equal(3, merged[0].Points.Count);

        // 合并间隔为 0 时按点位分别读取。
        var individual = MelsecA1EAcquisitionRunner.BuildReadPlan(selectors, maxMergeGap: 0);
        Assert.Equal(3, individual.Count);
    }

    [Fact]
    public void Melsec_ReadPlanKeepsBitAndWordReadsSeparate()
    {
        var selectors = new[] { "M:10:boolean", "D:10:int16" }
            .ToDictionary(
                selector => selector,
                selector =>
                {
                    AcquisitionSelectors.TryParseMelsec(selector, out var point, out _);
                    return point!;
                },
                StringComparer.Ordinal);

        var plan = MelsecA1EAcquisitionRunner.BuildReadPlan(selectors, maxMergeGap: 8);
        Assert.Equal(2, plan.Count);
        Assert.Single(plan, item => item.BitRead && item.Device == "M");
        Assert.Single(plan, item => !item.BitRead && item.Device == "D");
    }

    [Fact]
    public void Melsec_BitReadFrameUsesCommandZero()
    {
        var frame = MelsecA1EAcquisitionRunner.BuildBitReadFrame(
            " M"u8.ToArray(), address: 10, pointCount: 4, timer: 0x0010, layout: "A");
        Assert.Equal(0x00, frame[0]);
        Assert.Equal(0xFF, frame[1]);
        Assert.Equal(4, frame[^2]);

        var wordFrame = MelsecA1EAcquisitionRunner.BuildWordReadFrame(
            " D"u8.ToArray(), address: 10, wordCount: 4, timer: 0x0010, layout: "A");
        Assert.Equal(0x01, wordFrame[0]);
    }

    [Fact]
    public void Melsec_BitPayloadRejectsValuesOtherThanZeroAndOne()
    {
        Assert.Equal([false, true], MelsecA1EAcquisitionRunner.DecodeBitPayload("01"u8, 2, "ascii"));
        Assert.Throws<InvalidDataException>(() =>
            MelsecA1EAcquisitionRunner.DecodeBitPayload("02"u8, 2, "ascii"));
        Assert.Equal([true, false], MelsecA1EAcquisitionRunner.DecodeBitPayload([0x10], 2, "binary"));
        Assert.Throws<InvalidDataException>(() =>
            MelsecA1EAcquisitionRunner.DecodeBitPayload([0x20], 2, "binary"));
    }

    [Fact]
    public void RegisterStringDecodersRejectUndeclaredEncodings()
    {
        Assert.Throws<System.Text.DecoderFallbackException>(() =>
            ModbusTcpAcquisitionRunner.Decode([0xC328], new AcquisitionValueMapping
            {
                DataItemCode = "part.code",
                SourcePath = "holding-register:40:string:2",
                SourceDataType = "string",
                SourceByteLength = 2,
                ModbusArea = "holding-register",
                ModbusAddress = 40,
                ModbusQuantity = 1
            }));

        Assert.True(AcquisitionSelectors.TryParseMelsec("D:40:string:2", out var point, out var error), error);
        Assert.Throws<InvalidDataException>(() =>
            MelsecA1EAcquisitionRunner.Decode([0x81, 0x00, 0xFF, 0x00], point!));
    }

    [Fact]
    public void Melsec_DecodesWordDeviceBitOffset()
    {
        // D100 = 0b0000_0000_0000_1000 → 第 3 位为 1，其余为 0。
        var response = new byte[] { 0x81, 0x00, 0x08, 0x00 };
        AcquisitionSelectors.TryParseMelsec("D:100.3:boolean", out var third, out _);
        AcquisitionSelectors.TryParseMelsec("D:100.2:boolean", out var second, out _);
        Assert.Equal(true, MelsecA1EAcquisitionRunner.Decode(response, third!));
        Assert.Equal(false, MelsecA1EAcquisitionRunner.Decode(response, second!));
    }

    [Fact]
    public void Modbus_DecodesRegisterBitOffset()
    {
        var value = ModbusTcpAcquisitionRunner.Decode([0b0000_0000_0010_0000], new AcquisitionValueMapping
        {
            DataItemCode = "press.enabled",
            SourcePath = "holding-register:100.5:boolean",
            SourceDataType = "boolean",
            ModbusArea = "holding-register",
            ModbusAddress = 100,
            ModbusQuantity = 1,
            BitIndex = 5
        });
        Assert.Equal(true, value);
    }
}
