#!/bin/bash
# Acer Predator Thermal Tuning — applied at boot
# Caps CPU power to prevent thermal saturation while keeping good performance

# CPU Power Limits (RAPL)
# PL1 (sustained): 65W (down from 115W) — keeps all-core stress at ~82°C vs 93°C
# PL2 (burst): 80W (down from 157W) — still allows turbo for single-thread snappiness
# 13700HX base TDP is 55W, so 65W gives good turbo headroom without thermal saturation
echo 65000000 > /sys/class/powercap/intel-rapl:0/constraint_0_power_limit_uw
echo 80000000 > /sys/class/powercap/intel-rapl:0/constraint_1_power_limit_uw

echo "RAPL power limits set: PL1=65W PL2=80W"
