using OrganizationEmpire.Core.Model;

namespace OrganizationEmpire.Core.Util
{
    /// <summary>Generates fictional names, nicknames and backgrounds flavored by faction type.</summary>
    public static class NameGenerator
    {
        private static readonly string[] MaleFirst =
        {
            "Andre", "Marcus", "Tyrell", "Luis", "Carlos", "Mikey", "Dante", "Jamal", "Victor", "Sal", "Tony", "Frankie",
            "Nico", "Eddie", "Ray", "Leon", "Omar", "Darnell", "Hector", "Rico", "Vince", "Joey", "Paulie", "Gino",
            "Isaac", "Malik", "Trevon", "Kenny", "Danny", "Mateo", "Javier", "Sergei", "Owen", "Wade", "Cole", "Hank",
            "Reggie", "Theo", "Lamont", "Desmond", "Aaron", "Bobby", "Curtis", "Dmitri", "Emilio", "Felix", "Grant"
        };

        private static readonly string[] FemaleFirst =
        {
            "Tasha", "Maria", "Keisha", "Rosa", "Gina", "Angela", "Monique", "Lucia", "Nadia", "Carmen", "Dana", "Tina",
            "Vanessa", "Isabel", "Jade", "Leah", "Renee", "Sofia", "Brianna", "Elena", "Kim", "Valeria", "Toni", "Mara"
        };

        private static readonly string[] StreetLast =
        {
            "Johnson", "Williams", "Brooks", "Jackson", "Harris", "Moreno", "Ramirez", "Davis", "Price", "Green", "Carter",
            "Mitchell", "Reyes", "Flores", "Washington", "Coleman", "Bishop", "Lewis", "Hayes", "Torres", "Wright"
        };

        private static readonly string[] MafiaLast =
        {
            "Benedetti", "Moretti", "Calabrese", "Romano", "Lombardi", "DeLuca", "Esposito", "Gallo", "Rinaldi", "Marchetti",
            "Bruno", "Ferraro", "Vitale", "Caruso", "Fontana", "Santoro", "Greco", "Mancini", "Barone", "Conti"
        };

        private static readonly string[] MilitiaLast =
        {
            "Hale", "Mercer", "Kowalski", "Brennan", "Okafor", "Vance", "Steele", "Holt", "Ruiz", "Novak", "Fischer",
            "Grady", "Lancaster", "Petrov", "Walsh", "Quinn", "Ryder", "Sullivan", "Takahashi", "Castillo"
        };

        private static readonly string[] StreetNick =
        {
            "Lil' Ghost", "Smoke", "Tiny", "Shadow", "Loco", "Ice", "Bones", "Spider", "Trigger", "Dice", "Flaco",
            "Junebug", "Static", "Biscuit", "Nitro", "Slim", "Blaze", "Chino", "Kilo", "Ricochet"
        };

        private static readonly string[] MafiaNick =
        {
            "the Butcher", "Two Times", "the Accountant", "Fat Sal", "the Priest", "Knuckles", "the Ghost", "Sunshine",
            "the Tailor", "Little Nicky", "the Gentleman", "Ice Pick", "the Professor", "Big Tuna", "Sneakers"
        };

        private static readonly string[] MilitiaNick =
        {
            "Hawk", "Ranger", "Doc", "Sparks", "Bulldog", "Viper", "Echo", "Atlas", "Wolf", "Nomad", "Ghost", "Rook",
            "Patch", "Gunny", "Tank", "Sierra", "Mako", "Grizzly", "Saint", "Brick"
        };

        private static readonly string[] Hometowns =
        {
            "Davis", "Strawberry", "Chamberlain Hills", "Rancho", "La Mesa", "Mirror Park", "Little Seoul", "Vespucci",
            "Sandy Shores", "Paleto Bay", "Grapeseed", "East Vinewood", "Cypress Flats", "Harmony", "Del Perro", "Murrieta Heights"
        };

        private static readonly string[] StreetBackgrounds =
        {
            "Grew up on the block and ran with the local crew since middle school.",
            "Former mechanic who got tired of fixing other people's rides for pennies.",
            "Did a stretch at Bolingbroke and came out with nothing but a reputation.",
            "Used to sell mixtapes outside the liquor store; knows every corner by name.",
            "Lost a cousin to a rival set and has never let it go.",
            "Quiet kid from the projects with a talent for not getting caught.",
        };

        private static readonly string[] MafiaBackgrounds =
        {
            "Third generation in the life; his grandfather ran numbers in the old neighborhood.",
            "Former restaurant manager who discovered a talent for 'collections'.",
            "Ex-boxer who now settles disputes with fewer rounds.",
            "Accountant by training, keeps the family's books cleaner than the law's.",
            "Came over young, worked the docks, and earned respect the hard way.",
            "Smooth talker who used to run a car dealership as a front.",
        };

        private static readonly string[] MilitiaBackgrounds =
        {
            "Former private security contractor disillusioned with corporate warfare.",
            "Ex-National Guard who believes the state has abandoned the county.",
            "Survivalist from Blaine County with a cabin full of supplies.",
            "Ex-paramedic who now runs field medicine for the unit.",
            "Former Merryweather recruit who quit after one too many dirty jobs.",
            "Hunting guide with a sharpshooter's eye and a talent for staying unseen.",
        };

        public static string FirstName(Rng rng, bool female) => rng.Pick(female ? FemaleFirst : MaleFirst);

        public static string LastName(Rng rng, FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return rng.Pick(MafiaLast);
                case FactionType.Militia: return rng.Pick(MilitiaLast);
                default: return rng.Pick(StreetLast);
            }
        }

        public static string Nickname(Rng rng, FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return rng.Pick(MafiaNick);
                case FactionType.Militia: return rng.Pick(MilitiaNick);
                default: return rng.Pick(StreetNick);
            }
        }

        public static string Hometown(Rng rng) => rng.Pick(Hometowns);

        public static string Background(Rng rng, FactionType type)
        {
            switch (type)
            {
                case FactionType.Mafia: return rng.Pick(MafiaBackgrounds);
                case FactionType.Militia: return rng.Pick(MilitiaBackgrounds);
                default: return rng.Pick(StreetBackgrounds);
            }
        }

        public static string CivilianBackground(Rng rng)
        {
            string[] jobs =
            {
                "Works double shifts at a fast food joint and hates every minute of it.",
                "Delivery driver drowning in debt.",
                "Out-of-work construction worker looking for something steady.",
                "Bartender who hears everything and says nothing.",
                "Struggling actor who never got the Vinewood break.",
                "Bored office worker who dreams of a more dangerous life.",
                "Retired boxer with nothing left to prove.",
                "Street vendor who knows half the neighborhood.",
            };
            return rng.Pick(jobs);
        }
    }
}
