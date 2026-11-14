package com.waterphysics.config;

import com.waterphysics.WaterPhysics;
import net.minecraftforge.common.config.Config;
import net.minecraftforge.common.config.ConfigManager;
import net.minecraftforge.fml.client.event.ConfigChangedEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;

/**
 * All settings of the mod. Stored in config/waterphysics.cfg and editable in game through
 * Mods > Ultra Realistic Water Physics > Config (or the "Water Physics settings" key binding).
 */
@Config(modid = WaterPhysics.MODID, name = WaterPhysics.MODID, category = "")
@Config.LangKey("waterphysics.config.title")
public final class WPConfig {
    private WPConfig() {}

    @Config.Name("simulation")
    @Config.Comment("Water simulation (server side).")
    @Config.LangKey("waterphysics.config.simulation")
    public static final Simulation simulation = new Simulation();

    @Config.Name("physics")
    @Config.Comment("Physical constants and model coefficients (server side).")
    @Config.LangKey("waterphysics.config.physics")
    public static final Physics physics = new Physics();

    @Config.Name("performance")
    @Config.Comment("Performance limits (server side).")
    @Config.LangKey("waterphysics.config.performance")
    public static final Performance performance = new Performance();

    @Config.Name("gameplay")
    @Config.Comment("Interaction with vanilla water, buckets and dispensers (server side).")
    @Config.LangKey("waterphysics.config.gameplay")
    public static final Gameplay gameplay = new Gameplay();

    @Config.Name("pipes")
    @Config.Comment("Pipes, pumps and valves (server side).")
    @Config.LangKey("waterphysics.config.pipes")
    public static final Pipes pipes = new Pipes();

    @Config.Name("visuals")
    @Config.Comment("Graphics and effects (client side).")
    @Config.LangKey("waterphysics.config.visuals")
    public static final Visuals visuals = new Visuals();

    public static final class Simulation {
        @Config.Comment("Master switch for the physical water simulation.")
        @Config.LangKey("waterphysics.config.enabled")
        public boolean enabled = true;

        @Config.Comment("Simulation speed multiplier (1.0 = real time, 0.5 = slow motion, 2.0 = twice as fast).")
        @Config.RangeDouble(min = 0.05, max = 4.0)
        @Config.LangKey("waterphysics.config.timeScale")
        public double timeScale = 1.0;

        @Config.Comment("Simulation sub-steps per game tick. More steps = more accurate and stable but slower.")
        @Config.RangeInt(min = 1, max = 16)
        @Config.LangKey("waterphysics.config.substeps")
        public int substepsPerTick = 3;

        @Config.Comment("Pressure solver iterations per sub-step for enclosed (pressurized) water. Higher = more precise pressure.")
        @Config.RangeInt(min = 1, max = 16)
        @Config.LangKey("waterphysics.config.pressureIterations")
        public int pressureIterations = 2;

        @Config.Comment("Ticks between server -> client detail syncs (velocities, impacts) for visual effects.")
        @Config.RangeInt(min = 1, max = 40)
        @Config.LangKey("waterphysics.config.syncInterval")
        public int syncInterval = 4;

        @Config.Comment("Radius in blocks around each player in which water detail data is sent for visual effects.")
        @Config.RangeInt(min = 8, max = 256)
        @Config.LangKey("waterphysics.config.syncRadius")
        public int syncRadius = 48;
    }

    public static final class Physics {
        @Config.Comment("Gravitational acceleration in m/s^2 (Earth = 9.81).")
        @Config.RangeDouble(min = 0.5, max = 50.0)
        @Config.LangKey("waterphysics.config.gravity")
        public double gravity = 9.81;

        @Config.Comment("Linear (viscous) damping of flow in 1/s. Higher = thicker, slower water.")
        @Config.RangeDouble(min = 0.0, max = 20.0)
        @Config.LangKey("waterphysics.config.viscosity")
        public double viscosity = 1.0;

        @Config.Comment("Discharge coefficient Cd of openings (orifices). Torricelli: Q = Cd * A * sqrt(2 g h). Sharp-edged orifice ~ 0.61.")
        @Config.RangeDouble(min = 0.1, max = 1.0)
        @Config.LangKey("waterphysics.config.discharge")
        public double dischargeCoefficient = 0.62;

        @Config.Comment("Bottom / wall friction coefficient for shallow flow (higher = shallow water slows down more).")
        @Config.RangeDouble(min = 0.0, max = 5.0)
        @Config.LangKey("waterphysics.config.friction")
        public double surfaceFriction = 0.08;

        @Config.Comment("Surface tension approximation: a water layer thinner than this (liters per block) does not spread onto dry ground.")
        @Config.RangeDouble(min = 0.0, max = 500.0)
        @Config.LangKey("waterphysics.config.minSpread")
        public double minSpreadLiters = 15.0;

        @Config.Comment("Smallest amount of water (liters) kept in a block. Smaller droplets merge into neighbouring water.")
        @Config.RangeDouble(min = 0.01, max = 50.0)
        @Config.LangKey("waterphysics.config.minVolume")
        public double minVolumeLiters = 0.5;

        @Config.Comment("Fraction of the momentum of water hitting a wall/floor converted to pressure (bounce and splash strength).")
        @Config.RangeDouble(min = 0.0, max = 2.0)
        @Config.LangKey("waterphysics.config.impact")
        public double impactPressure = 1.0;

        @Config.Comment("Momentum carry-over (0..1): how strongly moving water keeps its direction (jets from holes, travelling waves).")
        @Config.RangeDouble(min = 0.0, max = 1.0)
        @Config.LangKey("waterphysics.config.momentum")
        public double momentum = 0.5;

        @Config.Comment("Compressibility stiffness: extra head (m) per 100% of over-filling of a block. Keeps pressure propagation stable.")
        @Config.RangeDouble(min = 2.0, max = 200.0)
        @Config.LangKey("waterphysics.config.stiffness")
        public double compressionStiffness = 25.0;

        @Config.Comment("Allow isolated tiny droplets (below minVolumeLiters) to evaporate. Evaporated water is counted in /water stats.")
        @Config.LangKey("waterphysics.config.evaporation")
        public boolean evaporateDroplets = false;
    }

    public static final class Performance {
        @Config.Comment("Maximum number of 16x16x16 sections simulated per tick (active regions). Extra sections wait (state is kept).")
        @Config.RangeInt(min = 1, max = 20000)
        @Config.LangKey("waterphysics.config.maxSections")
        public int maxActiveSections = 400;

        @Config.Comment("Only simulate water within this many chunks of a player (0 = everywhere loaded). Water elsewhere is frozen, not lost.")
        @Config.RangeInt(min = 0, max = 64)
        @Config.LangKey("waterphysics.config.activeRadius")
        public int activeChunkRadius = 0;

        @Config.Comment("A section stops being simulated after this many ticks without significant change (water settled).")
        @Config.RangeInt(min = 5, max = 1200)
        @Config.LangKey("waterphysics.config.sleepTicks")
        public int sleepTicks = 40;

        @Config.Comment("Volume change per tick (liters) below which a block is considered settled.")
        @Config.RangeDouble(min = 0.001, max = 10.0)
        @Config.LangKey("waterphysics.config.sleepEpsilon")
        public double sleepEpsilonLiters = 0.05;

        @Config.Comment("Maximum world block changes (water appearing/disappearing/level change) applied per tick. The rest is applied over the next ticks.")
        @Config.RangeInt(min = 64, max = 100000)
        @Config.LangKey("waterphysics.config.maxBlockUpdates")
        public int maxBlockUpdatesPerTick = 4000;

        @Config.Comment("Number of visual height levels (8 or 16). Fewer levels = fewer chunk re-renders.")
        @Config.RangeInt(min = 8, max = 16)
        @Config.LangKey("waterphysics.config.levels")
        public int visualLevels = 16;
    }

    public static final class Gameplay {
        @Config.Comment("Water buckets and dispensers place finite physical water (1000 L) and empty buckets take exactly 1000 L.")
        @Config.LangKey("waterphysics.config.buckets")
        public boolean physicalBuckets = true;

        @Config.Comment("Convert vanilla water into physical water when it is disturbed (a neighbouring block changes).")
        @Config.LangKey("waterphysics.config.convertVanilla")
        public boolean convertVanillaWater = true;

        @Config.Comment("Undisturbed vanilla water bodies (oceans, lakes, rivers) act as infinite reservoirs at their level. If false they are converted and drained like any other water (realistic but can be expensive for oceans).")
        @Config.LangKey("waterphysics.config.infiniteNatural")
        public boolean naturalWaterIsInfinite = true;

        @Config.Comment("Prevent vanilla 'infinite water source' creation.")
        @Config.LangKey("waterphysics.config.noInfinite")
        public boolean disableInfiniteSources = true;
    }

    public static final class Pipes {
        @Config.Comment("Maximum head (meters of water) a powered pump can produce. 10 m = ~98 kPa.")
        @Config.RangeDouble(min = 1.0, max = 200.0)
        @Config.LangKey("waterphysics.config.pumpHead")
        public double pumpMaxHead = 16.0;

        @Config.Comment("Maximum flow of a pump in liters per second.")
        @Config.RangeDouble(min = 1.0, max = 5000.0)
        @Config.LangKey("waterphysics.config.pumpFlow")
        public double pumpMaxFlow = 60.0;

        @Config.Comment("Darcy friction factor of pipes (resistance). Typical 0.015 - 0.04.")
        @Config.RangeDouble(min = 0.001, max = 1.0)
        @Config.LangKey("waterphysics.config.pipeFriction")
        public double frictionFactor = 0.025;

        @Config.Comment("Pipes accumulate damage above their pressure rating, crack (slow leak) and finally burst.")
        @Config.LangKey("waterphysics.config.pipeDamage")
        public boolean pipeDamage = true;

        @Config.Comment("Damage per second at twice the rated pressure (1.0 = burst after ~1 s).")
        @Config.RangeDouble(min = 0.0, max = 10.0)
        @Config.LangKey("waterphysics.config.damageRate")
        public double damageRate = 0.25;
    }

    public static final class Visuals {
        @Config.Comment("Graphics quality preset: 0 = low, 1 = medium, 2 = high, 3 = ultra.")
        @Config.RangeInt(min = 0, max = 3)
        @Config.LangKey("waterphysics.config.quality")
        public int quality = 2;

        @Config.Comment("Animated wave surface overlay (ripples, small waves, surface deformation).")
        @Config.LangKey("waterphysics.config.waves")
        public boolean waves = true;

        @Config.Comment("Wave grid resolution per block (2..8). Higher = smoother waves, more GPU work.")
        @Config.RangeInt(min = 2, max = 8)
        @Config.LangKey("waterphysics.config.waveQuality")
        public int waveResolution = 4;

        @Config.Comment("Particle effects: splashes, spray, foam, mist, whirlpool bubbles.")
        @Config.LangKey("waterphysics.config.particles")
        public boolean particles = true;

        @Config.Comment("Foam on impacts and fast flow.")
        @Config.LangKey("waterphysics.config.foam")
        public boolean foam = true;

        @Config.Comment("Whirlpool (vortex) effect above drains and holes in floors.")
        @Config.LangKey("waterphysics.config.vortex")
        public boolean vortex = true;

        @Config.Comment("Particle density multiplier.")
        @Config.RangeDouble(min = 0.0, max = 4.0)
        @Config.LangKey("waterphysics.config.particleDensity")
        public double particleDensity = 1.0;

        @Config.Comment("Distance in blocks within which water effects are rendered.")
        @Config.RangeInt(min = 8, max = 128)
        @Config.LangKey("waterphysics.config.effectsRadius")
        public int effectsRadius = 40;

        @Config.Comment("Render falling water as streams whose thickness depends on the flow (waterfalls).")
        @Config.LangKey("waterphysics.config.streams")
        public boolean thinStreams = true;

        @Config.Comment("Show the hydrometer HUD (volume, level, pressure, flow speed) while holding the Hydrometer.")
        @Config.LangKey("waterphysics.config.hud")
        public boolean hydrometerHud = true;
    }

    @Mod.EventBusSubscriber(modid = WaterPhysics.MODID)
    public static final class Sync {
        @SubscribeEvent
        public static void onChanged(ConfigChangedEvent.OnConfigChangedEvent e) {
            if (WaterPhysics.MODID.equals(e.getModID())) {
                ConfigManager.sync(WaterPhysics.MODID, Config.Type.INSTANCE);
            }
        }
    }
}
