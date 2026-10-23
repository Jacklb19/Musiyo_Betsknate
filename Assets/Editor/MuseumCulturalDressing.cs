using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static MusiyoBetsknate.Editor.MuseumDecorKit;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The decoration of PART A of INVESTIGACION_DECORACION_Y_GUIAS_20261022.md, approved by the owner on 23/10/2026, room by
    /// room, each piece with the sensitivity its source asks for. Built with the rest of <see cref="MuseumWorldDressing"/>
    /// when the scene is processed, so the saved scene stays as delivered. Nothing here is a person, a mask, a ritual
    /// object or a meal served before an empty seat; the loom, the sashes and the banners are plain stripes and colours,
    /// never figures; no rooster, no feathers, no petals falling on the visitor. Pieces the research leaves to a later
    /// decision are not built: the castle's own posts (A3.2, to be checked against its photograph), the patio of the
    /// instruments room (A4.5, waiting with the floor's figure of eight), the form of the hearth's fire (A5.6) and the
    /// optional river (A6.3) and horn call (A1.6). The hearth's own pieces, set right by U2, are not moved.
    /// </summary>
    public static class MuseumCulturalDressing
    {
        public const string RootName = "MuseumDecor";
        private static readonly Quaternion Upright = Quaternion.identity;
        private static Quaternion Y(float degrees) => Quaternion.Euler(0, degrees, 0);

        public static void Apply(Transform world, System.Random random)
        {
            // MUSIYO_DECOR=off leaves the museum as it was, for pictures before and after.
            if (System.Environment.GetEnvironmentVariable("MUSIYO_DECOR") == "off") return;
            var solid = MuseumDecorKit.Material(false);
            var leaves = MuseumDecorKit.Material(true);
            if (solid == null || leaves == null)
            {
                Debug.LogWarning("Museum decoration: the palette is missing (Musiyo > Museum World > Build Decoration Palette); nothing built.");
                return;
            }
            var root = new GameObject(RootName).transform;
            root.SetParent(world, false);
            Room(root, "Arrival", solid, leaves, (kit, foliage, colliders) => Arrival(kit, foliage, colliders, random));
            Room(root, "Welcome", solid, leaves, (kit, foliage, colliders) => Welcome(kit, foliage, colliders, random));
            Room(root, "Characters", solid, leaves, (kit, foliage, colliders) => Characters(kit, foliage, colliders, random));
            Room(root, "Instruments", solid, leaves, (kit, foliage, colliders) => Instruments(kit, foliage, colliders, random));
            Room(root, "Hearth", solid, leaves, (kit, foliage, colliders) => Hearth(kit, foliage, colliders, random));
            Room(root, "Overlook", solid, leaves, (kit, foliage, colliders) => Overlook(kit, foliage, colliders, random));
            int triangles = root.GetComponentsInChildren<MeshFilter>().Sum(filter => filter.sharedMesh.triangles.Length / 3);
            Debug.Log("Museum decoration: " + triangles + " triangles in " + root.childCount + " rooms.");
        }

        /// <summary>A solid box that keeps the visitor out of a piece, with no drawing of its own.</summary>
        private sealed class Solids
        {
            public readonly List<(string name, Vector3 centre, Quaternion turn, Vector3 size)> Boxes = new List<(string, Vector3, Quaternion, Vector3)>();
            public void Add(Vector3 centre, Quaternion turn, Vector3 size, string name = "DecorSolid") => Boxes.Add((name, centre, turn, size));
        }

        private static void Room(Transform root, string name, Material solid, Material leaves, System.Action<MuseumDecorKit, MuseumDecorKit, Solids> build)
        {
            var kit = new MuseumDecorKit();
            var foliage = new MuseumDecorKit();
            var colliders = new Solids();
            build(kit, foliage, colliders);
            var room = new GameObject("Decor_" + name).transform;
            room.SetParent(root, false);
            foreach (var (part, material, shapes) in new[] { ("Pieces", solid, kit), ("Leaves", leaves, foliage) })
            {
                if (shapes.Empty) continue;
                var item = MuseumWorldDressing.MeshObject(room, part, shapes.Mesh("Decor_" + name + "_" + part), material);
                item.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var (boxName, centre, turn, size) in colliders.Boxes)
            {
                var box = new GameObject(boxName).AddComponent<BoxCollider>();
                box.transform.SetParent(room, false);
                box.transform.SetPositionAndRotation(centre, turn);
                box.size = size;
            }
        }

        private static float Range(System.Random random, float from, float to) => from + (float)random.NextDouble() * (to - from);

        /// <summary>The height of the floor or the ground under a point, from the museum's own colliders.</summary>
        internal static float Ground(float x, float z, float fallback = 0)
        {
            foreach (var hit in Physics.RaycastAll(new Vector3(x, 60, z), Vector3.down, 120, ~0, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
            {
                string name = hit.collider.name;
                if (name.Contains("Piso") || name == MuseumWorldDressing.GroundName || name.StartsWith("Terreno") || name.StartsWith("Sendero")) return hit.point.y;
            }
            return fallback;
        }

        // ---- Shared pieces ------------------------------------------------------------------------------------------

        /// <summary>A basket of woven fibre: a wide low vessel with a rim, its mouth open.</summary>
        private static void Basket(MuseumDecorKit kit, Vector3 foot, float radius, float height, int colour = Fibre)
            => kit.Lathe(foot, Upright, new[] { new Vector2(radius * .7f, 0), new Vector2(radius, height * .8f), new Vector2(radius * 1.04f, height),
                new Vector2(radius * .95f, height), new Vector2(radius * .9f, height * .3f), new Vector2(0, height * .25f) }, colour, 9);

        /// <summary>A clay pot, its mouth narrower than its belly; with a lid when it is covered.</summary>
        private static void Pot(MuseumDecorKit kit, Vector3 foot, float radius, float height, bool covered, int colour = Clay)
        {
            kit.Lathe(foot, Upright, new[] { new Vector2(radius * .45f, 0), new Vector2(radius, height * .45f), new Vector2(radius * .7f, height * .85f),
                new Vector2(radius * .62f, height), new Vector2(radius * .55f, height), new Vector2(radius * .5f, height * .8f) }, colour, 10);
            if (covered) kit.Lathe(foot + Vector3.up * height * .97f, Upright, new[] { new Vector2(radius * .66f, 0), new Vector2(radius * .45f, height * .08f), new Vector2(0, height * .1f) }, ClayDark, 10);
        }

        /// <summary>A half gourd (totuma), cut and dried, used as a bowl.</summary>
        private static void Totuma(MuseumDecorKit kit, Vector3 foot, float radius)
            => kit.Lathe(foot, Upright, new[] { new Vector2(0, 0), new Vector2(radius * .7f, radius * .15f), new Vector2(radius, radius * .55f),
                new Vector2(radius * .93f, radius * .6f), new Vector2(radius * .62f, radius * .2f), new Vector2(0, radius * .12f) }, Gourd, 10);

        /// <summary>A short log lying along a direction, its bark around and its cut ends lighter.</summary>
        private static void Log(MuseumDecorKit kit, Vector3 from, Vector3 to, float radius)
            => kit.Cylinder(from, to, radius, radius * .92f, Bark, 7, true, WoodLight);

        /// <summary>A bundle of leaves lying on the ground, tied in the middle.</summary>
        private static void LeafBundle(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 middle, float heading, float length, System.Random random, int colour)
        {
            var along = Y(heading) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, along);
            for (int index = 0; index < 11; index++)
            {
                var foot = middle - along * length * .45f + side * Range(random, -.08f, .08f) + Vector3.up * Range(random, .03f, .14f);
                foliage.Leaf(foot, along + Vector3.up * Range(random, -.05f, .05f) + side * Range(random, -.1f, .1f), Vector3.up, length * Range(random, .85f, 1.05f),
                    Range(random, .1f, .16f), index % 2 == 0 ? PalmDry : colour, -.05f, 3);
            }
            kit.Cylinder(middle + Vector3.up * .08f - side * .14f, middle + Vector3.up * .08f + side * .14f, .1f, .1f, Fibre, 8, false);
        }

        // ---- Room 1: arrival ----------------------------------------------------------------------------------------

        /// <summary>
        /// A1.1 part of the maize field becomes a jajañ: raised beds (guachos) of cabbages, squash and achira, beans climbing
        /// the maize, low fruit trees by the boundary; no borrachero or plants of power. A1.2 a few hens and ducks among the
        /// beds, never a rooster. A1.3 a woodpile and bundles of palm leaves outside the gate, as material only.
        /// A1.4 palms and ferns on the slopes beyond the clearing.
        /// </summary>
        private static void Arrival(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            const float bedsNear = BedsNear, bedsFar = BedsFar;
            // Switched off rather than hidden, so the plants are left out when the field is combined into one mesh.
            foreach (var plant in BedMaize()) plant.gameObject.SetActive(false);
            float[] beds = { 3.9f, 5.15f, 6.4f };
            for (int bed = 0; bed < beds.Length; bed++)
            {
                float x = beds[bed], length = bedsNear - bedsFar;
                float floor = Ground(x, (bedsNear + bedsFar) * .5f);
                // A long mound of turned earth, wider at its foot, with clods along its flanks.
                kit.Frustum(new Vector3(x, floor - .03f, (bedsNear + bedsFar) * .5f), Upright, new Vector2(.95f, length + .2f), new Vector2(.62f, length - .1f), .25f, EarthDark);
                for (float z = bedsNear - .2f; z > bedsFar; z -= .55f)
                    foreach (float side in new[] { -1f, 1f })
                        kit.Ellipsoid(new Vector3(x + side * Range(random, .3f, .4f), floor + .06f, z + Range(random, -.2f, .2f)), Y(Range(random, 0, 360)),
                            new Vector3(.07f, .04f, .06f), Earth, 2, 5);
                float top = floor + .21f;
                for (float z = bedsNear - .6f; z > bedsFar + .4f; z -= bed == 2 ? 1.45f : 1.1f)
                {
                    var spot = new Vector3(x + Range(random, -.12f, .12f), top, z + Range(random, -.15f, .15f));
                    if (bed == 0) CabbagePlant(kit, foliage, spot, random);
                    else if (bed == 1) SquashPlant(kit, foliage, spot, random, z < bedsNear - 2 && z > bedsFar + 2);
                    else AchiraPlant(foliage, spot, random);
                }
            }
            // Low fruit trees inside the boundary: tree tomato and lulo (naranjilla).
            FruitTree(kit, foliage, new Vector3(7.7f, Ground(7.7f, -34.4f), -34.4f), 2.1f, TreeTomato, random);
            FruitTree(kit, foliage, new Vector3(7.7f, Ground(7.7f, -39.2f), -39.2f), 1.4f, Lulo, random);
            // Beans climb the maize on the other side of the path, as the climbing bean needs a stalk to climb.
            foreach (var plant in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(item => item.name == MuseumSetDressing.MaizeName && item.bounds.center.x < -2f && item.bounds.center.z < -32.4f && item.bounds.center.z > -45.6f))
                Beans(kit, foliage, new Vector3(plant.bounds.center.x, plant.bounds.min.y, plant.bounds.center.z), plant.bounds.size.y, random);
            // Chilacuán (papayuela) by the boundary on that side.
            ChilacuanTree(kit, foliage, new Vector3(-7.7f, Ground(-7.7f, -37.2f), -37.2f), random);
            // Hens and ducks between the beds, far from the gate and the house.
            foreach (var (spot, heading, pecking, colour) in new[] { (new Vector3(4.55f, 0, -35.3f), 40f, true, Horn), (new Vector3(5.8f, 0, -37.7f), 200f, false, HenWhite),
                         (new Vector3(4.5f, 0, -39.9f), 310f, true, Horn) })
                Hen(kit, new Vector3(spot.x, Ground(spot.x, spot.z), spot.z), heading, pecking, colour);
            foreach (var (spot, heading) in new[] { (new Vector3(5.75f, 0, -41.95f), 150f), (new Vector3(6.35f, 0, -42.35f), 120f) })
                Duck(kit, new Vector3(spot.x, Ground(spot.x, spot.z), spot.z), heading);
            // Outside the gate: firewood split and stacked, and bundles of palm leaves brought down from the heights.
            var pile = new Vector3(-3.0f, Ground(-3f, -59.4f), -59.4f);
            for (int layer = 0; layer < 3; layer++)
                for (int index = 0; index < 4 - layer; index++)
                {
                    float x = pile.x + (index - (3 - layer) * .5f) * .21f;
                    float y = pile.y + .1f + layer * .17f;
                    Log(kit, new Vector3(x, y, pile.z - .55f), new Vector3(x + Range(random, -.03f, .03f), y, pile.z + .55f), Range(random, .08f, .1f));
                }
            solids.Add(pile + Vector3.up * .3f, Upright, new Vector3(1f, .6f, 1.2f));
            LeafBundle(kit, foliage, new Vector3(-2.0f, Ground(-2f, -60.4f), -60.4f), 75, 1.7f, random, Palm);
            LeafBundle(kit, foliage, new Vector3(-3.7f, Ground(-3.7f, -60.7f), -60.7f), 100, 1.6f, random, Palm);
            // Wax palms and ferns on the slopes, where the palm of the ramos grows.
            foreach (var (x, z, height) in new[] { (-34f, -62f, 19f), (31f, -66f, 21f), (-44f, -35f, 23f), (40f, -20f, 20f), (-38f, 6f, 22f), (36f, 18f, 24f) })
                PalmTree(kit, foliage, new Vector3(x, Ground(x, z), z), height, random);
            foreach (var (x, z) in new[] { (-9.6f, -48f), (9.6f, -52f), (-9.8f, -30.5f), (9.7f, -44f) })
                Fern(foliage, new Vector3(x, Ground(x, z), z), random);
        }

        /// <summary>The right side of the path, from the house towards the gate, gives its maize to the beds.</summary>
        internal const float BedsNear = -32.6f, BedsFar = -41.4f, BedsWest = 3.2f;

        internal static IEnumerable<Transform> BedMaize()
            => Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(item => item.name == MuseumSetDressing.MaizeName
                && item.position.x > BedsWest && item.position.z < BedsNear + .4f && item.position.z > BedsFar - .4f).ToList();

        private static void CabbagePlant(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, System.Random random)
        {
            kit.Ellipsoid(foot + Vector3.up * .12f, Y(Range(random, 0, 360)), new Vector3(.14f, .12f, .14f), Cabbage, 4, 7);
            for (int index = 0; index < 7; index++)
            {
                var out1 = Y(index * 51 + Range(random, -10, 10)) * Vector3.forward;
                foliage.Leaf(foot + Vector3.up * .05f, out1 + Vector3.up * .35f, Vector3.Cross(Vector3.up, out1), .32f, .26f, index % 2 == 0 ? LeafMid : Cabbage, .12f, 2);
            }
        }

        private static void SquashPlant(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, System.Random random, bool fruit)
        {
            for (int index = 0; index < 5; index++)
            {
                var out1 = Y(index * 72 + Range(random, -20, 20)) * Vector3.forward;
                var stem = foot + out1 * Range(random, .1f, .35f);
                foliage.Leaf(stem + Vector3.up * .02f, out1 + Vector3.up * .9f, Vector3.Cross(Vector3.up, out1), .32f, .34f, index % 2 == 0 ? Greenery : LeafMid, .2f, 2);
            }
            if (fruit) kit.Ellipsoid(foot + new Vector3(.2f, .11f, 0), Y(Range(random, 0, 180)), new Vector3(.17f, .12f, .14f), Squash, 4, 8);
        }

        private static void AchiraPlant(MuseumDecorKit foliage, Vector3 foot, System.Random random)
        {
            for (int index = 0; index < 6; index++)
            {
                var out1 = Y(index * 60 + Range(random, -15, 15)) * Vector3.forward;
                foliage.Leaf(foot, Vector3.up * 2.6f + out1, Vector3.Cross(Vector3.up, out1), Range(random, .9f, 1.35f), .26f, index % 2 == 0 ? LeafDark : Greenery, .35f, 4);
            }
        }

        private static void FruitTree(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, float height, int fruit, System.Random random)
        {
            kit.Cylinder(foot, foot + Vector3.up * height * .7f, .05f, .03f, Bark, 6);
            var crown = foot + Vector3.up * height * .72f;
            for (int index = 0; index < 11; index++)
            {
                var out1 = Y(index * 33 + Range(random, -10, 10)) * Vector3.forward;
                foliage.Leaf(crown + Vector3.up * Range(random, -.1f, .15f), out1 + Vector3.up * Range(random, .1f, .6f), Vector3.Cross(Vector3.up, out1),
                    Range(random, .45f, .65f), .3f, index % 3 == 0 ? LeafDark : Greenery, .25f, 3);
            }
            for (int index = 0; index < 6; index++)
            {
                var out1 = Y(index * 60 + 20) * Vector3.forward;
                kit.Ellipsoid(crown + out1 * Range(random, .12f, .3f) - Vector3.up * Range(random, .05f, .2f), Upright, new Vector3(.035f, .05f, .035f), fruit, 3, 6);
            }
        }

        private static void ChilacuanTree(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, System.Random random)
        {
            kit.Cylinder(foot, foot + Vector3.up * 2.1f, .07f, .05f, Bark, 6);
            var top = foot + Vector3.up * 2.1f;
            for (int index = 0; index < 9; index++)
            {
                var out1 = Y(index * 40 + Range(random, -10, 10)) * Vector3.forward;
                foliage.Leaf(top, out1 + Vector3.up * .35f, Vector3.Cross(Vector3.up, out1), Range(random, .55f, .75f), .38f, index % 2 == 0 ? Greenery : LeafMid, .3f, 3);
            }
            for (int index = 0; index < 5; index++)
                kit.Ellipsoid(top + Y(index * 72) * Vector3.forward * .1f - Vector3.up * (.15f + index * .06f), Upright, new Vector3(.045f, .07f, .045f), Chilacuan, 3, 6);
        }

        private static void Beans(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, float height, System.Random random)
        {
            float turn = Range(random, 0, 360);
            // The vine winds up the stalk; its heart-shaped leaves come in threes, lighter than the maize, with hanging pods.
            for (int step = 0; step < 9; step++)
            {
                float t = (step + 1) / 10f;
                var out1 = Y(turn + step * 75) * Vector3.forward;
                var spot = foot + Vector3.up * (height * .88f * t) + out1 * .06f;
                kit.Cylinder(spot - Vector3.up * height * .1f - out1 * .03f, spot, .006f, .006f, LeafLight, 4, false);
                for (int leaf = -1; leaf <= 1; leaf++)
                {
                    var way = Y(turn + step * 75 + leaf * 35) * Vector3.forward;
                    foliage.Leaf(spot, way + Vector3.up * .15f, Vector3.Cross(Vector3.up, way), .15f, .13f, LeafLight, .08f, 2);
                }
                if (step % 2 == 1) kit.Cylinder(spot + out1 * .04f, spot + out1 * .06f - Vector3.up * .14f, .01f, .008f, Cabbage, 4, false);
            }
        }

        private static void PalmTree(MuseumDecorKit kit, MuseumDecorKit foliage, Vector3 foot, float height, System.Random random)
        {
            kit.Cylinder(foot, foot + Vector3.up * height, .22f, .16f, WallLight, 6, false);
            var top = foot + Vector3.up * height;
            for (int index = 0; index < 9; index++)
            {
                var out1 = Y(index * 40 + Range(random, -10, 10)) * Vector3.forward;
                foliage.Leaf(top, out1 + Vector3.up * .25f, Vector3.Cross(Vector3.up, out1), Range(random, 2.6f, 3.4f), .7f, index % 3 == 0 ? PalmDry : Palm, 1.1f, 4);
            }
        }

        private static void Fern(MuseumDecorKit foliage, Vector3 foot, System.Random random)
        {
            for (int index = 0; index < 8; index++)
            {
                var out1 = Y(index * 45 + Range(random, -15, 15)) * Vector3.forward;
                foliage.Leaf(foot, out1 + Vector3.up * .8f, Vector3.Cross(Vector3.up, out1), Range(random, .6f, .85f), .2f, index % 2 == 0 ? LeafDark : Greenery, .35f, 3);
            }
        }

        /// <summary>A hen of the jajañ: round body, small comb, short tail; pecking or looking about. Never a rooster.</summary>
        private static void Hen(MuseumDecorKit kit, Vector3 foot, float heading, bool pecking, int colour)
        {
            var turn = Y(heading);
            Vector3 P(float x, float y, float z) => foot + turn * new Vector3(x, y, z);
            kit.Ellipsoid(P(0, .24f, 0), turn * Quaternion.Euler(pecking ? 18 : -6, 0, 0), new Vector3(.15f, .14f, .2f), colour, 5, 8);
            // Folded wings a shade darker, and a short tail held up as one narrow fan.
            foreach (float side in new[] { -1f, 1f })
                kit.Ellipsoid(P(side * .12f, .25f, -.02f), turn * Quaternion.Euler(-8, 0, 0), new Vector3(.035f, .09f, .14f), colour == HenWhite ? Cream : Hide, 3, 6);
            kit.Ellipsoid(P(0, .33f, -.2f), turn * Quaternion.Euler(-30, 0, 0), new Vector3(.02f, .09f, .07f), colour == HenWhite ? Cream : WoodDark, 3, 6);
            var head = pecking ? P(0, .19f, .24f) : P(0, .43f, .16f);
            kit.Ellipsoid(head, turn, new Vector3(.045f, .05f, .05f), colour, 4, 6);
            kit.Box(head + turn * new Vector3(0, .05f, 0), turn, new Vector3(.012f, .025f, .04f), Comb);
            kit.Cylinder(head + turn * new Vector3(0, 0, .04f), head + turn * new Vector3(0, -.01f, .075f), .012f, 0, Beak, 4, false);
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Cylinder(P(side * .045f, .12f, 0), P(side * .05f, 0, .01f), .011f, .009f, Beak, 4, false);
                kit.Ellipsoid(head + turn * new Vector3(side * .035f, .012f, .02f), turn, Vector3.one * .008f, Eye, 2, 4);
            }
        }

        private static void Duck(MuseumDecorKit kit, Vector3 foot, float heading)
        {
            var turn = Y(heading);
            Vector3 P(float x, float y, float z) => foot + turn * new Vector3(x, y, z);
            kit.Ellipsoid(P(0, .14f, 0), turn, new Vector3(.12f, .1f, .2f), HenWhite, 5, 8);
            kit.Cylinder(P(0, .18f, .13f), P(0, .3f, .17f), .035f, .03f, HenWhite, 6, false);
            kit.Ellipsoid(P(0, .33f, .18f), turn, new Vector3(.045f, .045f, .06f), HenWhite, 4, 6);
            kit.Box(P(0, .32f, .25f), turn, new Vector3(.04f, .015f, .06f), Orange);
            foreach (float side in new[] { -1f, 1f }) kit.Ellipsoid(P(side * .035f, .345f, .2f), turn, Vector3.one * .008f, Eye, 2, 4);
        }

        // ---- Room 2: welcome ----------------------------------------------------------------------------------------

        /// <summary>
        /// A1.4 reeds (totora) in the lagoon's edge. A2.1 a basket of petals at rest on a bench, never falling. A2.2 a loom
        /// (guanga) with a sayo of plain stripes half woven, its basket of yarn and picking sticks, by the myth. A2.3 the
        /// Inga corner: folded clothes on a bench (pacha, rolled chumbe without figures, tupuye, baita, beads) and a basket
        /// of cobs and chilacuanes; no maize-husk doll. A2.4 chicha in a covered pot with two totumas on a low table, beside
        /// a bench and never in front of an empty seat.
        /// </summary>
        private static void Welcome(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            var centre = new Vector3(0, 0, -20);
            foreach (float degrees in new[] { 25f, 62f, 140f, 205f, 238f, 318f })
            {
                var out1 = new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0, Mathf.Sin(degrees * Mathf.Deg2Rad));
                var foot = centre + out1 * 2.72f + Vector3.up * .14f;
                for (int index = 0; index < 9; index++)
                {
                    var lean = out1 * Range(random, -.15f, .25f) + Y(index * 40) * Vector3.forward * .12f + Vector3.up;
                    foliage.Leaf(foot + Y(index * 40) * Vector3.forward * Range(random, .02f, .12f), lean, Y(index * 40) * Vector3.right,
                        Range(random, .8f, 1.35f), .035f, index % 3 == 0 ? PalmDry : LeafLight, .05f, 3);
                }
            }
            // The basket of petals rests at the end of the bench by the names, where the visitor arrives.
            var petals = new Vector3(-5.1f, .46f, -20.55f);
            Basket(kit, petals, .15f, .13f);
            int[] colours = { PetalPink, PetalMagenta, PetalYellow, PetalWhite, Red };
            for (int index = 0; index < 26; index++)
            {
                var spot = petals + Vector3.up * .125f + Y(Range(random, 0, 360)) * Vector3.forward * Mathf.Sqrt(Range(random, 0, 1)) * .12f;
                kit.Ellipsoid(spot, Quaternion.Euler(Range(random, -30, 30), Range(random, 0, 360), 0), new Vector3(.025f, .008f, .018f), colours[index % colours.Length], 2, 4);
            }
            // The loom by the myth, facing the room.
            var loomAt = new Vector3(-2.53f, 0, -15.23f);
            var facing = (centre - loomAt); facing.y = 0;
            Loom(kit, loomAt, Quaternion.LookRotation(facing.normalized, Vector3.up), random);
            solids.Add(loomAt + Vector3.up * .9f, Quaternion.LookRotation(facing.normalized, Vector3.up), new Vector3(1.3f, 1.8f, .55f));
            // The Inga corner on the bench across the room.
            InkaClothes(kit, new Vector3(5.1f, .46f, -20.1f));
            var cobs = new Vector3(5.15f, 0, -21.3f);
            Basket(kit, cobs, .22f, .18f);
            // Cobs heaped lengthwise, a lower row and an upper one across it, and three chilacuanes on top.
            for (int row = 0; row < 2; row++)
                for (int index = 0; index < 4 - row; index++)
                {
                    float across = (index - (3 - row) * .5f) * .075f;
                    var spot = cobs + Vector3.up * (.15f + row * .055f) + Y(row * 90 + Range(random, -12, 12)) * new Vector3(across, 0, 0);
                    kit.Ellipsoid(spot, Quaternion.Euler(90, row * 90 + Range(random, -12, 12), 0), new Vector3(.034f, .095f, .034f), index % 3 == 1 ? Mote : Yellow, 3, 6);
                }
            for (int index = 0; index < 3; index++)
                kit.Ellipsoid(cobs + Vector3.up * .27f + Y(index * 120 + 20) * Vector3.forward * .05f, Upright, new Vector3(.04f, .055f, .04f), Chilacuan, 3, 6);
            // Chicha: a covered pot and two totumas on a low table beside the bench of the names.
            var table = new Vector3(-5.0f, 0, -22.15f);
            Table(kit, table, Y(90), new Vector3(.6f, .34f, .45f), WoodDark);
            Pot(kit, table + new Vector3(0, .34f, .08f), .13f, .3f, true);
            Totuma(kit, table + new Vector3(.05f, .34f, -.17f), .065f);
            Totuma(kit, table + new Vector3(-.13f, .34f, -.13f), .06f);
            solids.Add(table + Vector3.up * .2f, Y(90), new Vector3(.6f, .4f, .45f));
        }

        /// <summary>
        /// A vertical loom: two posts, a beam above and below, the warp tight between them, and the sayo woven from the foot
        /// in plain white with thin black stripes (PES p. 29); above it the warp still waits. No chumbe figures.
        /// </summary>
        private static void Loom(MuseumDecorKit kit, Vector3 foot, Quaternion turn, System.Random random)
        {
            Vector3 P(float x, float y, float z) => foot + turn * new Vector3(x, y, z);
            foreach (float side in new[] { -.6f, .6f }) kit.Cylinder(P(side, 0, 0), P(side, 1.75f, 0), .045f, .04f, Bark, 6);
            kit.Cylinder(P(-.68f, 1.6f, 0), P(.68f, 1.6f, 0), .035f, .035f, WoodDark, 6);
            kit.Cylinder(P(-.68f, .25f, 0), P(.68f, .25f, 0), .035f, .035f, WoodDark, 6);
            // The woven part, from the lower beam up, in stripes.
            float woven = .62f, y = .27f;
            int stripe = 0;
            while (y < .27f + woven)
            {
                float band = stripe % 2 == 0 ? .1f : .018f;
                kit.Box(P(0, y + band * .5f, .005f), turn, new Vector3(1.0f, band, .01f), stripe % 2 == 0 ? Wool : Black);
                y += band;
                stripe++;
            }
            // The warp above, thread by thread, and the sword stick and heddle across it.
            for (float x = -.48f; x <= .48f; x += .04f) kit.Box(P(x, (y + 1.58f) * .5f, 0), turn, new Vector3(.006f, 1.58f - y, .006f), Wool);
            kit.Box(P(0, y + .08f, .01f), turn, new Vector3(1.06f, .03f, .015f), WoodLight);
            kit.Cylinder(P(-.56f, y + .22f, -.015f), P(.56f, y + .22f, -.015f), .012f, .012f, WoodLight, 5);
            // The basket of yarn at its foot, with two picking sticks laid across.
            var basket = P(.82f, 0, .22f);
            Basket(kit, basket, .17f, .15f);
            for (int index = 0; index < 4; index++)
                kit.Ellipsoid(basket + Vector3.up * .15f + Y(index * 90 + 30) * Vector3.forward * .07f, Upright, Vector3.one * .055f, index % 2 == 0 ? Wool : Black, 3, 6);
            kit.Cylinder(basket + new Vector3(-.16f, .2f, -.03f), basket + new Vector3(.18f, .19f, .05f), .007f, .007f, WoodLight, 4);
            kit.Cylinder(basket + new Vector3(-.15f, .21f, .04f), basket + new Vector3(.17f, .2f, -.06f), .007f, .007f, WoodLight, 4);
        }

        /// <summary>Folded Inga clothes on a bench (Tisoy p. 52): pacha, tupuye, baita, a rolled chumbe of plain bands, beads.</summary>
        private static void InkaClothes(MuseumDecorKit kit, Vector3 seat)
        {
            // Cloths folded in two: thin layers, each a little askew, the fold rounded on one side.
            var stack = seat + new Vector3(.01f, 0, -.45f);
            foreach (var (lift, turn, size, colour) in new[] { (0f, 0f, new Vector2(.34f, .36f), Black), (.022f, 7f, new Vector2(.3f, .32f), Blue), (.04f, -5f, new Vector2(.29f, .3f), Purple) })
            {
                var layer = stack + Vector3.up * (lift + .005f);
                for (int ply = 0; ply < 2; ply++)
                    kit.Box(layer + Vector3.up * ply * .009f, Y(turn), new Vector3(size.x, .006f, size.y), colour);
                kit.Cylinder(layer + Y(turn) * new Vector3(-size.x * .5f, .0045f, -size.y * .5f), layer + Y(turn) * new Vector3(-size.x * .5f, .0045f, size.y * .5f), .0075f, .0075f, colour, 6, true);
            }
            // The chumbe, rolled, in plain bands only.
            var roll = seat + new Vector3(0, .05f, -.05f);
            int[] bands = { Red, Green, Wool, Yellow, Red };
            for (int index = 0; index < bands.Length; index++)
                kit.Cylinder(roll + new Vector3(-.12f + index * .048f, 0, 0), roll + new Vector3(-.072f + index * .048f, 0, 0), .05f, .05f, bands[index], 8, true);
            // Beads in two loops.
            for (int loop = 0; loop < 2; loop++)
                for (int index = 0; index < 22; index++)
                {
                    float angle = index * Mathf.PI * 2 / 22;
                    kit.Ellipsoid(seat + new Vector3(Mathf.Cos(angle) * (.09f + loop * .02f), .012f, .28f + Mathf.Sin(angle) * (.07f + loop * .015f)), Upright,
                        Vector3.one * .011f, loop == 0 ? BeadWhite : Red, 2, 4);
                }
        }

        private static void Table(MuseumDecorKit kit, Vector3 foot, Quaternion turn, Vector3 size, int colour)
        {
            kit.Box(foot + Vector3.up * (size.y - .02f), turn, new Vector3(size.x, .04f, size.z), colour);
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    kit.Box(foot + turn * new Vector3(x * (size.x * .5f - .04f), (size.y - .04f) * .5f, z * (size.z * .5f - .04f)), turn, new Vector3(.04f, size.y - .04f, .04f), colour);
        }

        // ---- Room 3: characters -------------------------------------------------------------------------------------

        /// <summary>
        /// A3.1 the castle being prepared, beside it and off the way: loose palm leaves, woven ramos rolled and laid, one of
        /// the seven poles on the ground with its ropes; nothing says who does it. A3.3 a carver's bench with a block of wood
        /// half shaped, gouges, chisels, an adze and shavings; no finished mask. A3.4 plain banners of bright cloth on a rack,
        /// without any emblem. A3.5 crowns of sashes (aro of palm, cotton thread, hanging fajas) on a corridor bench,
        /// apart from the characters; no feathers.
        /// </summary>
        private static void Characters(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            // The castle's preparation, east of it.
            var prep = new Vector3(3.15f, 0, 3.6f);
            // A pile of palm leaves cut to length, laid the same way, layer on layer.
            for (int index = 0; index < 32; index++)
            {
                var heading = 70 + Range(random, -10, 10);
                var foot = prep + Y(heading) * new Vector3(Range(random, -.3f, .3f), 0, -.65f) + Vector3.up * (.015f + index * .004f);
                foliage.Leaf(foot, Y(heading) * Vector3.forward + Vector3.up * .03f, Vector3.up, Range(random, 1.15f, 1.4f), Range(random, .14f, .2f), index % 3 == 0 ? PalmDry : Palm, -.02f, 3);
            }
            kit.Cylinder(prep + Y(70) * new Vector3(-.36f, .08f, 0), prep + Y(70) * new Vector3(.36f, .08f, 0), .02f, .02f, Fibre, 5, false);
            // Ramos woven into mats: one laid flat, one rolled.
            var mat = prep + new Vector3(.9f, .03f, -.55f);
            kit.Box(mat, Y(20), new Vector3(1.0f, .04f, .55f), Straw);
            for (int rib = 0; rib < 8; rib++) kit.Box(mat + Y(20) * new Vector3(-.44f + rib * .125f, .025f, 0), Y(20), new Vector3(.025f, .02f, .55f), PalmDry);
            kit.Cylinder(prep + new Vector3(-.2f, .12f, -.8f), prep + new Vector3(.45f, .12f, -.95f), .12f, .12f, Straw, 9, true, PalmDry);
            // One of the poles, on the ground with its rope.
            var pole = new Vector3(2.4f, .09f, 4.95f);
            kit.Cylinder(pole, pole + new Vector3(3.1f, 0, .15f), .09f, .075f, Wood, 8, true, WoodLight);
            for (int coil = 0; coil < 3; coil++) kit.Cylinder(pole + new Vector3(.6f + coil * .05f, .0f, -.1f), pole + new Vector3(.6f + coil * .05f, 0, .1f), .1f, .1f, Fibre, 8, false);
            solids.Add(pole + new Vector3(1.55f, .02f, .07f), Upright, new Vector3(3.2f, .22f, .3f));
            // Banners on their rack, west of the castle: plain cloth of bright colours, no emblem.
            var rack = new Vector3(-3.15f, 0, 3.7f);
            kit.Box(rack + new Vector3(0, .32f, 0), Upright, new Vector3(1.5f, .06f, .12f), WoodDark);
            kit.Box(rack + new Vector3(0, .05f, 0), Upright, new Vector3(1.6f, .1f, .45f), WoodDark);
            int[] cloths = { Red, Blue, Green, Yellow, Wool };
            for (int index = 0; index < cloths.Length; index++)
            {
                float x = -.6f + index * .3f;
                var bottom = rack + new Vector3(x, .1f, 0);
                var top = rack + new Vector3(x + Range(random, -.05f, .05f), 2.35f, .25f);
                kit.Cylinder(bottom, top, .018f, .015f, Wood, 5);
                var up = (top - bottom).normalized;
                var hang = top - up * .08f;
                kit.Sheet(hang, hang + new Vector3(.5f, -.05f, .02f), hang + new Vector3(.48f, -.42f, .04f), hang - up * .38f, cloths[index]);
                kit.Sheet(hang - up * .38f, hang + new Vector3(.48f, -.42f, .04f), hang + new Vector3(.5f, -.05f, .02f), hang, cloths[index]);
            }
            solids.Add(rack + Vector3.up * .6f, Upright, new Vector3(1.6f, 1.2f, .5f));
            // The carver's bench, east, near the masks: a block half shaped and its tools.
            var bench = new Vector3(8.25f, 0, -3.0f);
            var turn = Y(-90);
            Table(kit, bench, turn, new Vector3(1.2f, .74f, .6f), Wood);
            // A log on its side, still in its bark at one end; the other end roughed out and its top split flat.
            var block = bench + Vector3.up * .74f;
            var lengthwise = turn * Vector3.right;
            kit.Cylinder(block + Vector3.up * .13f - lengthwise * .25f, block + Vector3.up * .13f, .13f, .13f, Hide, 9, true, WoodLight);
            // The hewn half: flat facets left by the adze, no shape yet.
            kit.Cylinder(block + Vector3.up * .13f, block + Vector3.up * .125f + lengthwise * .2f, .125f, .11f, WoodLight, 5, true, Shaving);
            kit.Box(block + Vector3.up * .25f - lengthwise * .1f, turn, new Vector3(.28f, .02f, .11f), Shaving);
            for (int tool = 0; tool < 4; tool++)
            {
                var start = block + turn * new Vector3(-.45f + tool * .08f, .01f, .22f);
                kit.Cylinder(start, start + turn * new Vector3(0, 0, -.11f), .012f, .012f, WoodDark, 5);
                kit.Cylinder(start + turn * new Vector3(0, 0, -.11f), start + turn * new Vector3(0, 0, -.2f), .006f, .004f, Metal, 4);
            }
            // The adze leans on the bench's leg; shavings on the floor.
            kit.Cylinder(bench + turn * new Vector3(.62f, .02f, .1f), bench + turn * new Vector3(.66f, .5f, .2f), .016f, .014f, WoodDark, 5);
            kit.Box(bench + turn * new Vector3(.66f, .52f, .26f), turn, new Vector3(.035f, .03f, .11f), Metal);
            for (int index = 0; index < 18; index++)
            {
                var spot = bench + Y(Range(random, 0, 360)) * Vector3.forward * Range(random, .1f, .55f) + Vector3.up * .01f;
                kit.Ellipsoid(spot, Quaternion.Euler(Range(random, 0, 40), Range(random, 0, 360), 0), new Vector3(.03f, .006f, .012f), Shaving, 2, 4);
            }
            solids.Add(bench + Vector3.up * .4f, turn, new Vector3(1.3f, .8f, .7f));
            // Crowns of sashes on the west corridor's bench, apart from the characters.
            foreach (var (z, threads) in new[] { (5.15f, new[] { Yellow, Blue, Red }), (5.7f, new[] { Red, Yellow, Blue }) })
                Crown(kit, new Vector3(-12.3f, .46f, z), threads);
        }

        /// <summary>Where a bench's seat ends going one way from a point on it, read from the bench's own collider.</summary>
        private static float SeatEdge(Vector3 seat, Vector3 outward, float fallback)
        {
            for (float reach = 0; reach < .6f; reach += .01f)
            {
                var probe = seat + outward * reach + Vector3.up * .3f;
                if (!Physics.Raycast(probe, Vector3.down, out var hit, .5f, ~0, QueryTriggerInteraction.Ignore) || hit.point.y < seat.y - .05f) return Mathf.Max(0, reach - .01f);
            }
            return fallback;
        }

        /// <summary>
        /// A crown of sashes as the PES describes it (p. 30): a ring of palm fibre wound with yellow, blue and red cotton,
        /// and woven sashes hanging from it, laid on a bench. Only sashes, no feathers: they lie side by side towards the
        /// room and fall over the seat's edge, never spread around the ring.
        /// </summary>
        private static void Crown(MuseumDecorKit kit, Vector3 seat, int[] threads)
        {
            const int segments = 12;
            for (int index = 0; index < segments; index++)
            {
                float a0 = index * Mathf.PI * 2 / segments, a1 = (index + 1) * Mathf.PI * 2 / segments;
                var p0 = seat + new Vector3(Mathf.Cos(a0) * .11f, .03f, Mathf.Sin(a0) * .11f);
                var p1 = seat + new Vector3(Mathf.Cos(a1) * .11f, .03f, Mathf.Sin(a1) * .11f);
                kit.Cylinder(p0, p1, .022f, .022f, threads[index % threads.Length], 5, false);
            }
            // The bench stands against the west wall; the room is east of it.
            float edge = Mathf.Max(.15f, SeatEdge(seat, Vector3.right, .2f) + .004f);
            int[] sashes = { Red, Green, Yellow, Blue, Purple, Wool };
            for (int index = 0; index < sashes.Length; index++)
            {
                float z = (index - (sashes.Length - 1) * .5f) * .042f;
                float start = .07f, fall = .26f + (index % 3) * .03f;
                kit.Box(seat + new Vector3((start + edge) * .5f, .012f + index * .0015f, z), Upright, new Vector3(edge - start, .004f, .036f), sashes[index]);
                kit.Box(seat + new Vector3(edge + .003f, .012f - fall * .5f, z), Upright, new Vector3(.004f, fall, .036f), sashes[index]);
            }
        }

        // ---- Room 4: instruments and dance --------------------------------------------------------------------------

        /// <summary>
        /// A4.1 instruments left on a bench as if the players rested: a log drum with hide, flutes and a rondador of tunda,
        /// a horn, a string of seed rattles; never the Matachín's bell. A4.3 a tortoise shell (morrocoy) among them.
        /// A4.2 the makers' corner: tunda stalks, a hollowed trunk drying, two horns, a totuma of seeds. A4.4 small
        /// instruments on a wall shelf, made by nobody named.
        /// </summary>
        private static void Instruments(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            // On the bench by the south wall, and the drum beside it.
            var seat = new Vector3(20.5f, .46f, -9.42f);
            Drum(kit, new Vector3(21.85f, 0, -9.3f), .22f, .38f);
            for (int flute = 0; flute < 2; flute++)
                kit.Cylinder(seat + new Vector3(.95f, .015f + flute * .02f, -.06f + flute * .07f), seat + new Vector3(.45f, .015f, -.03f + flute * .07f), .012f, .011f, Straw, 6);
            Rondador(kit, seat + new Vector3(.2f, .01f, .02f), Y(90), .5f);
            CowHorn(kit, seat + new Vector3(-.15f, .03f, -.05f), Y(10), .3f);
            for (int seed = 0; seed < 12; seed++)
                kit.Ellipsoid(seat + new Vector3(-.45f + seed * .018f, .015f, .08f + Mathf.Sin(seed * .9f) * .02f), Upright, Vector3.one * .014f, Gourd, 2, 5);
            // The morrocoy among them.
            kit.Ellipsoid(seat + new Vector3(-.7f, .05f, 0), Y(25), new Vector3(.13f, .06f, .16f), Hide, 4, 8);
            kit.Ellipsoid(seat + new Vector3(-.7f, .015f, 0), Y(25), new Vector3(.135f, .02f, .165f), WoodDark, 2, 8);
            solids.Add(new Vector3(21.85f, .2f, -9.3f), Upright, new Vector3(.5f, .42f, .5f));
            // The makers' corner by the east wall.
            var corner = new Vector3(25.45f, 0, -6.6f);
            for (int stalk = 0; stalk < 12; stalk++)
            {
                var foot = corner + new Vector3(Range(random, -.12f, .05f), 0, Range(random, -.2f, .2f));
                kit.Cylinder(foot, foot + new Vector3(.32f, 1.7f, Range(random, -.08f, .08f)), .013f, .01f, stalk % 3 == 0 ? Straw : WoodLight, 5);
            }
            var trunk = new Vector3(25.05f, .2f, -7.65f);
            kit.Cylinder(trunk + new Vector3(0, 0, -.45f), trunk + new Vector3(0, 0, .45f), .2f, .19f, WoodDark, 8, true, Wood);
            kit.Cylinder(trunk + new Vector3(0, 0, .44f), trunk + new Vector3(0, 0, .46f), .12f, .12f, Charred, 8, true);
            solids.Add(trunk, Upright, new Vector3(.45f, .42f, 1f));
            CowHorn(kit, corner + new Vector3(-.6f, .03f, .5f), Y(70), .32f);
            CowHorn(kit, corner + new Vector3(-.75f, .03f, .3f), Y(140), .28f);
            var seeds = corner + new Vector3(-.55f, 0, -.35f);
            Totuma(kit, seeds, .1f);
            for (int index = 0; index < 9; index++)
                kit.Ellipsoid(seeds + new Vector3(Range(random, -.05f, .05f), .055f, Range(random, -.05f, .05f)), Upright, Vector3.one * .012f, Gourd, 2, 4);
            // Small instruments on a shelf of the north wall.
            var shelf = new Vector3(20.7f, 1.3f, .52f);
            kit.Box(shelf, Upright, new Vector3(1.6f, .04f, .22f), WoodLight);
            foreach (float x in new[] { -.7f, .7f }) kit.Box(shelf + new Vector3(x, -.08f, .06f), Upright, new Vector3(.04f, .14f, .1f), WoodLight);
            Rondador(kit, shelf + new Vector3(-.55f, .02f, 0), Upright, .18f);
            Drum(kit, shelf + new Vector3(-.2f, .02f, 0), .05f, .08f);
            Drum(kit, shelf + new Vector3(-.05f, .02f, .03f), .04f, .06f);
            kit.Cylinder(shelf + new Vector3(.15f, .03f, 0), shelf + new Vector3(.45f, .03f, .02f), .006f, .005f, Straw, 5);
            CowHorn(kit, shelf + new Vector3(.6f, .03f, 0), Y(180), .12f);
        }

        private static void Drum(MuseumDecorKit kit, Vector3 foot, float radius, float height)
        {
            kit.Cylinder(foot, foot + Vector3.up * height, radius, radius, WoodDark, 10, true, Hide);
            for (int lace = 0; lace < 8; lace++)
            {
                float a = lace * Mathf.PI / 4, b = a + Mathf.PI / 8;
                kit.Cylinder(foot + new Vector3(Mathf.Cos(a) * radius * 1.02f, height * .05f, Mathf.Sin(a) * radius * 1.02f),
                    foot + new Vector3(Mathf.Cos(b) * radius * 1.02f, height * .95f, Mathf.Sin(b) * radius * 1.02f), .004f, .004f, Hide, 3, false);
            }
        }

        /// <summary>A rondador: pipes of tunda in a row, from long to short, bound across.</summary>
        private static void Rondador(MuseumDecorKit kit, Vector3 foot, Quaternion turn, float length)
        {
            for (int pipe = 0; pipe < 9; pipe++)
            {
                float l = length * (1 - pipe * .07f);
                var start = foot + turn * new Vector3(pipe * length * .045f, 0, 0);
                kit.Cylinder(start, start + turn * new Vector3(0, 0, -l), length * .02f, length * .02f, Straw, 5);
            }
            kit.Box(foot + turn * new Vector3(length * .18f, 0, -length * .2f), turn, new Vector3(length * .42f, length * .03f, length * .05f), Fibre);
        }

        /// <summary>A cow's horn, curving to its tip.</summary>
        private static void CowHorn(MuseumDecorKit kit, Vector3 foot, Quaternion turn, float length)
        {
            var a = foot; var b = foot + turn * new Vector3(0, length * .1f, length * .4f);
            var c = foot + turn * new Vector3(length * .12f, length * .25f, length * .75f); var d = foot + turn * new Vector3(length * .3f, length * .42f, length);
            kit.Cylinder(a, b, length * .12f, length * .09f, Horn, 7, true, Charred);
            kit.Cylinder(b, c, length * .09f, length * .06f, Horn, 7, false);
            kit.Cylinder(c, d, length * .06f, length * .01f, Horn, 7, false);
        }

        // ---- Room 5: hearth -----------------------------------------------------------------------------------------

        /// <summary>
        /// The hearth keeps the pieces U2 set right. Added beside them: A5.2 a jigra hung on the post, a biaco on the floor,
        /// totumas, a batea and wooden spoons on the cupboard. A5.3 cuyes in a corner behind the benches. A5.4 food being
        /// prepared by the cupboard (mote, potatoes, eggs, ají, beshan), never served on a table or before a seat.
        /// A5.5 a sleeping mat rolled by the wall.
        /// </summary>
        private static void Hearth(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            // The jigra of cabuya on the central post.
            var hook = new Vector3(.24f, 1.55f, 18.3f);
            kit.Cylinder(hook, hook + new Vector3(.02f, -.25f, 0), .006f, .006f, Fibre, 4, false);
            var bag = hook + new Vector3(.05f, -.42f, 0);
            kit.Ellipsoid(bag, Upright, new Vector3(.13f, .18f, .1f), Fibre, 4, 8);
            // The knotted mesh of the jigra: cords from the mouth to the foot, and rings across them.
            for (int cord = 0; cord < 8; cord++)
            {
                float a = cord * Mathf.PI / 4;
                Vector3 On(float height) => bag + new Vector3(Mathf.Cos(a) * .132f * Mathf.Sqrt(1 - height * height), height * .18f, Mathf.Sin(a) * .102f * Mathf.Sqrt(1 - height * height));
                for (int step = 0; step < 4; step++) kit.Cylinder(On(.9f - step * .45f), On(.9f - (step + 1) * .45f), .004f, .004f, WoodDark, 3, false);
            }
            foreach (float height in new[] { .5f, 0f, -.5f })
                for (int cord = 0; cord < 12; cord++)
                {
                    float a0 = cord * Mathf.PI / 6, a1 = (cord + 1) * Mathf.PI / 6, r = Mathf.Sqrt(1 - height * height) * 1.015f;
                    kit.Cylinder(bag + new Vector3(Mathf.Cos(a0) * .13f * r, height * .18f, Mathf.Sin(a0) * .1f * r),
                        bag + new Vector3(Mathf.Cos(a1) * .13f * r, height * .18f, Mathf.Sin(a1) * .1f * r), .004f, .004f, WoodDark, 3, false);
                }
            // The biaco and the food being prepared by the cupboard.
            var cupboardFloor = new Vector3(2.75f, .02f, 16.1f);
            Basket(kit, cupboardFloor + new Vector3(.1f, 0, 1.45f), .22f, .3f);
            var potatoes = cupboardFloor + new Vector3(-.05f, 0, .75f);
            Basket(kit, potatoes, .17f, .14f);
            for (int index = 0; index < 8; index++)
                kit.Ellipsoid(potatoes + new Vector3(Range(random, -.08f, .08f), .14f, Range(random, -.08f, .08f)), Y(Range(random, 0, 360)), new Vector3(.045f, .035f, .035f), Potato, 3, 5);
            var plate = cupboardFloor + new Vector3(-.15f, 0, .25f);
            kit.Lathe(plate, Upright, new[] { new Vector2(.06f, 0), new Vector2(.13f, .02f), new Vector2(.14f, .03f), new Vector2(0, .015f) }, Clay, 10);
            for (int egg = 0; egg < 6; egg++)
                kit.Ellipsoid(plate + new Vector3(Mathf.Cos(egg) * .06f, .045f, Mathf.Sin(egg) * .06f), Quaternion.Euler(90, egg * 40, 0), new Vector3(.024f, .032f, .024f), Egg, 3, 6);
            var aji = cupboardFloor + new Vector3(.15f, 0, .2f);
            Totuma(kit, aji, .07f);
            for (int index = 0; index < 6; index++)
                kit.Ellipsoid(aji + new Vector3(Range(random, -.03f, .03f), .04f, Range(random, -.03f, .03f)), Y(index * 60), new Vector3(.012f, .012f, .03f), Aji, 2, 5);
            var mote = cupboardFloor + new Vector3(-.2f, 0, -.35f);
            Pot(kit, mote, .17f, .32f, false);
            for (int grain = 0; grain < 14; grain++)
                kit.Ellipsoid(mote + new Vector3(Range(random, -.06f, .06f), .27f, Range(random, -.06f, .06f)), Upright, Vector3.one * .014f, Mote, 2, 4);
            var beshan = cupboardFloor + new Vector3(.25f, 0, -.55f);
            Pot(kit, beshan, .15f, .28f, false);
            for (int leaf = 0; leaf < 5; leaf++)
                kit.Ellipsoid(beshan + new Vector3(Range(random, -.05f, .05f), .24f, Range(random, -.05f, .05f)), Y(leaf * 72), new Vector3(.05f, .012f, .035f), Cabbage, 2, 5);
            // Totumas, a batea and spoons on top of the cupboard.
            var top = new Vector3(3.45f, 1.8f, 16.2f);
            Totuma(kit, top + new Vector3(0, 0, -.6f), .075f);
            Totuma(kit, top + new Vector3(0, 0, -.35f), .065f);
            kit.Lathe(top + new Vector3(0, 0, .2f), Upright, new[] { new Vector2(.12f, 0), new Vector2(.17f, .05f), new Vector2(.15f, .05f), new Vector2(0, .015f) }, Wood, 10);
            for (int spoon = 0; spoon < 2; spoon++)
            {
                var start = top + new Vector3(-.05f + spoon * .06f, .01f, .55f);
                kit.Cylinder(start, start + new Vector3(0, 0, .2f), .008f, .008f, WoodLight, 4);
                kit.Ellipsoid(start + new Vector3(0, .005f, .23f), Upright, new Vector3(.025f, .01f, .035f), WoodLight, 2, 5);
            }
            // Cuyes in the corner behind the west bench, with a little grass.
            foreach (var (spot, heading, colour) in new[] { (new Vector3(-3.05f, 0, 19.1f), 30f, Hide), (new Vector3(-3.35f, 0, 19.7f), 160f, Cream), (new Vector3(-2.85f, 0, 20.25f), 260f, HenBrown) })
                Cuy(kit, spot + Vector3.up * .02f, heading, colour);
            for (int blade = 0; blade < 12; blade++)
            {
                var foot = new Vector3(-3.2f, .02f, 19.55f) + Y(Range(random, 0, 360)) * Vector3.forward * Range(random, 0, .15f);
                var out1 = Y(Range(random, 0, 360)) * Vector3.forward;
                foliage.Leaf(foot, out1, Vector3.up, .22f, .03f, LeafLight, -.02f, 2);
            }
            // A sleeping mat rolled by the west wall.
            var mat = new Vector3(-3.55f, .11f, 17.6f);
            kit.Cylinder(mat, mat + new Vector3(0, 0, 1.15f), .1f, .1f, Straw, 10, true, PalmDry);
            foreach (float z in new[] { .2f, .95f }) kit.Cylinder(mat + new Vector3(0, 0, z), mat + new Vector3(0, 0, z + .03f), .103f, .103f, Fibre, 10, false);
        }

        private static void Cuy(MuseumDecorKit kit, Vector3 foot, float heading, int colour)
        {
            var turn = Y(heading);
            kit.Ellipsoid(foot + Vector3.up * .07f, turn, new Vector3(.08f, .07f, .13f), colour, 4, 8);
            kit.Ellipsoid(foot + turn * new Vector3(0, .08f, .11f), turn, new Vector3(.05f, .05f, .05f), colour, 3, 6);
            foreach (float side in new[] { -1f, 1f })
            {
                kit.Ellipsoid(foot + turn * new Vector3(side * .03f, .125f, .1f), turn, new Vector3(.015f, .012f, .008f), Hide, 2, 4);
                kit.Ellipsoid(foot + turn * new Vector3(side * .028f, .095f, .15f), turn, Vector3.one * .007f, Eye, 2, 4);
            }
        }

        // ---- Room 6: overlook ---------------------------------------------------------------------------------------

        internal const float OverlookBenchZ = 19.35f;

        /// <summary>
        /// The closing of the visit. A6.1 a plain bench of thick plank turned to the valley, by the credits, to sit and look:
        /// it is a seat of the museum like any other. A6.2 the land as the PES describes it, seen from here: a lagoon on the
        /// plain below and wax palms rising from the forest. The chagras on the slopes are left to the ridges themselves:
        /// the forest hides those slopes from the overlook, and pieces laid on them read as loose blocks from the arrival.
        /// No cross, no verse, no named mountain (A6.4).
        /// </summary>

        private static void Overlook(MuseumDecorKit kit, MuseumDecorKit foliage, Solids solids, System.Random random)
        {
            // Close to the parapet, so that seated eyes (seat and .72 m) still clear it and reach the plain below.
            var facing = Vector3.left;
            var walls = Physics.RaycastAll(new Vector3(-12f, .8f, OverlookBenchZ), facing, 8, ~0, QueryTriggerInteraction.Ignore).Where(hit => hit.point.x < -15.8f).ToList();
            float parapet = walls.Count > 0 ? walls.Max(hit => hit.point.x) : -16.9f;
            var seat = new Vector3(parapet + .62f, Ground(parapet + .62f, OverlookBenchZ), OverlookBenchZ);
            var turn = Quaternion.LookRotation(facing, Vector3.up);
            // Two thick planks side by side on two blocks.
            foreach (float across in new[] { -.105f, .105f })
                kit.Box(seat + turn * new Vector3(0, .43f, across), turn, new Vector3(1.8f, .09f, .2f), across < 0 ? Wood : WoodDark);
            foreach (float end in new[] { -1f, 1f })
                kit.Box(seat + turn * new Vector3(end * .68f, .19f, 0), turn, new Vector3(.14f, .38f, .38f), WoodDark);
            // A bench of the museum: «Sentarte» offers it, looking at the valley.
            solids.Add(seat + Vector3.up * .24f, turn, new Vector3(1.8f, .48f, .42f), "Bench");
            // A small lagoon on the plain below the overlook, with reeds at its edge.
            var lagoon = new Vector3(-27.5f, 0, 19f);
            lagoon.y = Ground(lagoon.x, lagoon.z) + .04f;
            const int sides = 18;
            for (int index = 0; index < sides; index++)
            {
                float a0 = index * Mathf.PI * 2 / sides, a1 = (index + 1) * Mathf.PI * 2 / sides;
                float r0 = 3.2f + Mathf.Sin(index * 1.7f) * .4f, r1 = 3.2f + Mathf.Sin((index + 1) * 1.7f) * .4f;
                var p0 = lagoon + new Vector3(Mathf.Cos(a0) * r0 * 1.3f, 0, Mathf.Sin(a0) * r0);
                var p1 = lagoon + new Vector3(Mathf.Cos(a1) * r1 * 1.3f, 0, Mathf.Sin(a1) * r1);
                p0.y = Mathf.Max(lagoon.y, Ground(p0.x, p0.z) + .03f);
                p1.y = Mathf.Max(lagoon.y, Ground(p1.x, p1.z) + .03f);
                var mid = new Vector3(lagoon.x, lagoon.y, lagoon.z);
                kit.Sheet(mid, p1, p0, mid, Lagoon);
                if (index % 3 == 0)
                    for (int reed = 0; reed < 7; reed++)
                        foliage.Leaf(p0 + Y(reed * 50) * Vector3.forward * .15f, Vector3.up + Y(reed * 50) * Vector3.forward * .15f, Y(reed * 50) * Vector3.right,
                            Range(random, .7f, 1.2f), .035f, reed % 3 == 0 ? PalmDry : LeafLight, .05f, 3);
            }
            // Wax palms rising from the forest to the west, where the ramos come from.
            foreach (var (x, z, height) in new[] { (-48f, 30f, 24f), (-56f, 8f, 27f), (-63f, 22f, 25f), (-52f, 44f, 22f), (-70f, -4f, 28f), (-46f, -6f, 23f), (-75f, 36f, 26f) })
                PalmTree(kit, foliage, new Vector3(x, Ground(x, z), z), height, random);
        }
    }
}
