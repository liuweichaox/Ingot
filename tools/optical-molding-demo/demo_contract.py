"""Shared field contract for the simulated optical-lens molding device."""

from __future__ import annotations

from copy import deepcopy


DATA_ITEMS = [
    {
        "code": "process.stage_number",
        "displayName": "阶段号",
        "dataType": "integer",
        "unit": None,
        "category": "stage",
        "nullable": False,
        "sourcePath": "stageNumber",
        "register": "D:1:uint16",
        "scale": 1,
    },
    {
        "code": "mold.upper_infrared_temperature",
        "displayName": "上模红外温度",
        "dataType": "double",
        "unit": "Cel",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.upperMold.infraredTemperature",
        "register": "D:101:int16",
        "scale": 0.1,
    },
    {
        "code": "heater.upper_current",
        "displayName": "上模电流",
        "dataType": "double",
        "unit": "A",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.upperMold.current",
        "register": "D:102:int16",
        "scale": 0.01,
    },
    {
        "code": "heater.upper_voltage",
        "displayName": "上模电压",
        "dataType": "double",
        "unit": "V",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.upperMold.voltage",
        "register": "D:103:int16",
        "scale": 0.1,
    },
    {
        "code": "mold.lower_infrared_temperature",
        "displayName": "下模红外温度",
        "dataType": "double",
        "unit": "Cel",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.lowerMold.infraredTemperature",
        "register": "D:104:int16",
        "scale": 0.1,
    },
    {
        "code": "heater.lower_current",
        "displayName": "下模电流",
        "dataType": "double",
        "unit": "A",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.lowerMold.current",
        "register": "D:105:int16",
        "scale": 0.01,
    },
    {
        "code": "heater.lower_voltage",
        "displayName": "下模电压",
        "dataType": "double",
        "unit": "V",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.lowerMold.voltage",
        "register": "D:106:int16",
        "scale": 0.1,
    },
    {
        "code": "molding.pressure_load",
        "displayName": "压力",
        "dataType": "double",
        "unit": "kg",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.pressure.load",
        "register": "D:107:int16",
        "scale": 0.1,
    },
    {
        "code": "grating.position",
        "displayName": "光栅位置",
        "dataType": "double",
        "unit": "mm",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.grating.position",
        "register": "D:108:int16",
        "scale": 0.001,
    },
    {
        "code": "servo.speed",
        "displayName": "伺服速度",
        "dataType": "double",
        "unit": "mm/s",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.servo.speed",
        "register": "D:109:int16",
        "scale": 0.01,
    },
    {
        "code": "vacuum.pressure",
        "displayName": "真空度",
        "dataType": "double",
        "unit": "kPa",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.vacuum.pressure",
        "register": "D:110:int16",
        "scale": 0.1,
    },
    {
        "code": "servo.position",
        "displayName": "伺服位置",
        "dataType": "double",
        "unit": "mm",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.servo.position",
        "register": "D:111:int16",
        "scale": 0.01,
    },
    {
        "code": "heater.upper_power",
        "displayName": "上模功率",
        "dataType": "double",
        "unit": "W",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.upperMold.power",
        "register": "D:112:int32",
        "scale": 0.1,
    },
    {
        "code": "heater.lower_power",
        "displayName": "下模功率",
        "dataType": "double",
        "unit": "W",
        "category": "process",
        "nullable": False,
        "sourcePath": "signals.lowerMold.power",
        "register": "D:114:int32",
        "scale": 0.1,
    },
]


RECIPE_PARAMETERS = [
    {
        "code": "recipe.heat_position",
        "displayName": "HEAT位置",
        "dataType": "double",
        "unit": "mm",
        "nullable": False,
        "sourcePath": "heatPosition",
        "baseline": 2.0,
        "register": "D:200:int16",
        "scale": 0.01,
    },
    {
        "code": "recipe.work_position",
        "displayName": "WORK位置",
        "dataType": "double",
        "unit": "mm",
        "nullable": False,
        "sourcePath": "workPosition",
        "baseline": 8.5,
        "register": "D:201:int16",
        "scale": 0.01,
    },
    {
        "code": "recipe.host_position",
        "displayName": "HOST位置",
        "dataType": "double",
        "unit": "mm",
        "nullable": False,
        "sourcePath": "hostPosition",
        "baseline": 25.0,
        "register": "D:202:int16",
        "scale": 0.01,
    },
    {
        "code": "recipe.upper_temperature_setpoint",
        "displayName": "上模设置温度",
        "dataType": "double",
        "unit": "Cel",
        "nullable": False,
        "sourcePath": "upperTemperatureSetpoint",
        "baseline": 620.0,
        "register": "D:203:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.lower_temperature_setpoint",
        "displayName": "下模设置温度",
        "dataType": "double",
        "unit": "Cel",
        "nullable": False,
        "sourcePath": "lowerTemperatureSetpoint",
        "baseline": 618.0,
        "register": "D:204:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.nitrogen_temperature",
        "displayName": "充氮气温度",
        "dataType": "double",
        "unit": "Cel",
        "nullable": False,
        "sourcePath": "nitrogenTemperature",
        "baseline": 25.0,
        "register": "D:205:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.preheat_soak_delay",
        "displayName": "预热保温延时",
        "dataType": "integer",
        "unit": "s",
        "nullable": False,
        "sourcePath": "preheatSoakDelaySeconds",
        "baseline": 120,
        "register": "D:206:uint16",
        "scale": 1,
    },
    {
        "code": "recipe.pressure_difference_upper_limit",
        "displayName": "压力差上限",
        "dataType": "double",
        "unit": "kg",
        "nullable": False,
        "sourcePath": "pressureDifferenceUpperLimit",
        "baseline": 20.0,
        "register": "D:207:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.upper_temperature_upper_limit",
        "displayName": "上模温度上限",
        "dataType": "double",
        "unit": "Cel",
        "nullable": False,
        "sourcePath": "upperTemperatureUpperLimit",
        "baseline": 630.0,
        "register": "D:208:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.lower_temperature_upper_limit",
        "displayName": "下模温度上限",
        "dataType": "double",
        "unit": "Cel",
        "nullable": False,
        "sourcePath": "lowerTemperatureUpperLimit",
        "baseline": 628.0,
        "register": "D:209:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.pressure_upper_limit",
        "displayName": "压力上限",
        "dataType": "double",
        "unit": "kg",
        "nullable": False,
        "sourcePath": "pressureUpperLimit",
        "baseline": 1300.0,
        "register": "D:210:int16",
        "scale": 0.1,
    },
    {
        "code": "recipe.work_position_pressure_setpoint",
        "displayName": "WORK位设定压力",
        "dataType": "double",
        "unit": "kg",
        "nullable": False,
        "sourcePath": "workPositionPressureSetpoint",
        "baseline": 1200.0,
        "register": "D:211:int16",
        "scale": 0.1,
    },
]


def data_item_definitions() -> list[dict[str, object]]:
    return [
        {
            key: value
            for key, value in item.items()
            if key not in {"sourcePath", "register", "scale"}
        }
        for item in deepcopy(DATA_ITEMS)
    ]


def recipe_parameter_definitions() -> list[dict[str, object]]:
    return [
        {
            key: value
            for key, value in item.items()
            if key not in {"sourcePath", "baseline", "register", "scale"}
        }
        for item in deepcopy(RECIPE_PARAMETERS)
    ]


def device_recipe_values(version: int) -> dict[str, float | int]:
    result = {
        item["sourcePath"]: item["baseline"]
        for item in RECIPE_PARAMETERS
    }
    if version >= 2:
        result["upperTemperatureSetpoint"] = 625.0
        result["workPositionPressureSetpoint"] = 1220.0
    return result


def platform_recipe_values(version: int) -> list[dict[str, object]]:
    device_values = device_recipe_values(version)
    return [
        {"code": item["code"], "value": device_values[item["sourcePath"]]}
        for item in RECIPE_PARAMETERS
    ]
