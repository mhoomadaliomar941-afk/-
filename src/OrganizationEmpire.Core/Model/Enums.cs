using System;

namespace OrganizationEmpire.Core.Model
{
    public enum FactionType { StreetGang, Mafia, Militia }

    public enum Difficulty { Easy, Normal, Hard, Brutal }

    public enum StartingProgression { Humble, Established, Powerful }

    /// <summary>Faction-wide personality: affects events, dialogue tone and some modifiers.</summary>
    public enum FactionPersonality { Ruthless, Honorable, Pragmatic, Flamboyant, Secretive, Zealous }

    /// <summary>Strategic posture: affects passive territory behavior and rival reactions.</summary>
    public enum FactionBehavior { Balanced, Aggressive, Defensive, Expansionist, Covert }

    /// <summary>
    /// Street gang side activities. These are abstract, fictional game systems (resource production
    /// and delivery missions), not real-world guidance.
    /// </summary>
    [Flags]
    public enum GangActivities { None = 0, Narcotics = 1, Arms = 2, Both = 3 }

    public enum Personality { Loyal, Aggressive, Calm, Nervous, Ambitious, Intelligent, Sarcastic, Cowardly, Disciplined, Rebellious }

    public enum MemberRole { Soldier, Enforcer, Bodyguard, Driver, Scout, Medic, Recruiter, Earner, Specialist, Guard }

    public enum MemberStatus { Active, Wounded, Dead, Deserted, Jailed }

    public enum OrderType { None, Follow, Stay, Guard, MoveTo, ReturnToHQ, EnterVehicle, ExitVehicle, Attack, DefendPlayer, DefendLocation, Standby }

    public enum FormationType { Default, Loose, Circle, LineAbreast, SingleFile, Surround }

    public enum SquadRole { Assault, Defense, Patrol, Escort, Security, Recon, Logistics }

    public enum LoadoutType { Unarmed, Melee, Sidearm, SMG, Shotgun, Rifle, Marksman, Heavy }

    public enum TerritoryType { Residential, Commercial, Industrial, Strategic, Rural }

    public enum RelationStatus { Neutral, Rivalry, War, Ceasefire, Peace, Alliance }

    public enum RivalKind { StreetGang, Mafia, Cartel, Biker, Paramilitary }

    [Flags]
    public enum RankPermission
    {
        None = 0,
        Recruit = 1,
        CommandSquads = 2,
        LeadSquad = 4,
        Promote = 8,
        Negotiate = 16,
        ManageResources = 32,
        Armory = 64,
        Leadership = 128,
        All = 255,
    }

    public enum EventSeverity { Info, Good, Warning, Bad, Critical }

    public enum MemoryKind { Interaction, Event, Promotion, Demotion, Order, Relationship, Loyalty, Mission, Background, Dialogue }

    public enum HqUpgrade { Armory, Barracks, Garage, Comms, Fortifications, Infirmary }

    public enum GuardBehavior { Static, Patrol, Alert }

    public enum BusinessKind { Restaurant, Nightclub, CarDealership, ConstructionFirm, Laundromat, Casino, ShippingCompany, BarberShop, Bodega, TowingCompany }

    public enum MissionCategory { Recruitment, Territory, Defense, Conflict, Resource, Escort, Interaction, Event, Campaign, Special }

    /// <summary>Runtime templates implemented by the GTA layer.</summary>
    public enum MissionTemplate { Assault, Defend, Delivery, Collect, Escort, Recruit, Recon, Meeting, Assassination, FinalCampaign }

    public enum FlagPattern { Solid, HorizontalBicolor, HorizontalTricolor, VerticalBicolor, VerticalTricolor, NordicCross, CenterCross, Bordered, Quartered, Canton, FiveStripes, CenterBand }

    public enum EmblemType { None, Monogram, Star, Crown, Spade, Club, Diamond, Heart, Shield, CustomImage }

    public enum GovernmentType { Republic, Council, Junta, Directorate, Syndicate, People }
}
