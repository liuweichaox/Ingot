// Ensures device recommendations remain separate from manufacturers and registered communication drivers.

import { describe, expect, it } from "vitest";
import { deviceProfileOptions } from "../src/acquisition/deviceProfileRegistry";
import { protocolOptions } from "../src/acquisition/protocolRegistry";

describe("采集设备类别与驱动推荐", () => {
  it("每个设备类别只引用已登记的通信驱动", () => {
    const registeredDrivers = new Set(protocolOptions.map(([id]) => id));
    const profileKinds = deviceProfileOptions.map(profile => profile.value);

    expect(new Set(profileKinds).size).toBe(profileKinds.length);
    expect(profileKinds).toEqual([
      "plc",
      "cnc",
      "industrial-robot",
      "dcs",
      "hmi-panel",
      "building-automation",
      "remote-io",
      "drive",
      "sensor-transmitter",
      "process-instrument",
      "measurement-instrument",
      "weighing-instrument",
      "energy-meter",
      "power-asset",
      "machine-vision",
      "barcode-rfid",
      "environmental-monitor",
      "industrial-computer",
      "industrial-gateway",
      "network-infrastructure",
      "scada-mes",
      "other",
    ]);
    expect(profileKinds).not.toContain("mitsubishi");

    for (const profile of deviceProfileOptions) {
      expect(profile.drivers.length, `${profile.value} 至少应给出一个接入驱动`).toBeGreaterThan(0);
      for (const driver of profile.drivers)
        expect(registeredDrivers.has(driver), `${profile.value} 推荐了未登记驱动 ${driver}`).toBe(true);
    }
  });
});
