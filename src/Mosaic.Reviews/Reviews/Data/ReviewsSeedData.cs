using Mosaic.Reviews.Model;

namespace Mosaic.Reviews.Data;

/// <summary>
/// Mosaic's reviews: 120 of them, spread unevenly over the 25 catalogued
/// products. Three products have none at all, which is what makes a nullable
/// average rating worth having. Since chapter 4 this list is the seed for a
/// PostgreSQL table rather than the store itself.
/// </summary>
public sealed class ReviewsSeedData
{
    /// <summary>
    /// Reviews' own copy of Catalog's identifier pattern, and below it Accounts'.
    /// The duplication is deliberate. Later in the book Catalog, Accounts and
    /// Reviews become separate services and the identifier is the only contract
    /// left between them, so Reviews derives its keys the same way the other two
    /// do rather than reaching into their seed data for them.
    /// </summary>
    private static Guid ProductId(int n) => new($"a0000000-0000-4000-8000-{n:D12}");

    /// <summary>Accounts' customer identifier pattern, duplicated for the same reason.</summary>
    private static Guid CustomerId(int n) => new($"c0000000-0000-4000-8000-{n:D12}");

    /// <summary>
    /// Reviews' own identifiers, with a first character no other domain uses so
    /// a misrouted identifier is obvious on sight.
    /// </summary>
    private static Guid ReviewId(int n) => new($"e0000000-0000-4000-8000-{n:D12}");

    /// <summary>
    /// Posting times are written out in full rather than computed from the clock.
    /// The schema snapshot and the sample queries have to give the same answer in
    /// a year's time as they do today.
    /// </summary>
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    public IReadOnlyList<Review> Reviews { get; } =
    [
        // Product 1, Larsen Oak Dining Table. The flagship, and reviewed like one.
        new(ReviewId(1), ProductId(1), CustomerId(3), 5, "Solid as anything. Took two of us to get it through the door.", At(2025, 4, 12, 9, 14)),
        new(ReviewId(2), ProductId(1), CustomerId(7), 4, "Lovely grain. One leg needed shimming on our floor.", At(2025, 5, 3, 18, 40)),
        new(ReviewId(3), ProductId(1), CustomerId(1), 5, "Six adults, Christmas dinner, no wobble.", At(2025, 5, 21, 11, 5)),
        new(ReviewId(4), ProductId(1), CustomerId(11), 5, null, At(2025, 6, 9, 8, 22)),
        new(ReviewId(5), ProductId(1), CustomerId(4), 3, "Beautiful table, but the matte finish marks if you put a hot pan on it. Ask me how I know.", At(2025, 6, 30, 20, 11)),
        new(ReviewId(6), ProductId(1), CustomerId(9), 4, "Arrived a week later than the estimate. Worth the wait.", At(2025, 7, 15, 14, 33)),
        new(ReviewId(7), ProductId(1), CustomerId(2), 5, "Third piece of Larsen we own. Still the best money we have spent on furniture.", At(2025, 8, 8, 10, 2)),
        new(ReviewId(8), ProductId(1), CustomerId(6), 2, "Mine came with a scratch across one end. Support sent a replacement top, so two stars rather than one.", At(2025, 9, 2, 16, 47)),
        new(ReviewId(9), ProductId(1), CustomerId(12), 5, "The oak has darkened a little in six months and I like it more than when it arrived.", At(2025, 10, 19, 9, 58)),
        new(ReviewId(10), ProductId(1), CustomerId(5), 4, "Assembly took twenty minutes with the supplied key.", At(2025, 11, 27, 19, 20)),
        new(ReviewId(11), ProductId(1), CustomerId(8), 5, "Seats six comfortably, seven if nobody minds elbows.", At(2026, 1, 16, 12, 41)),
        new(ReviewId(12), ProductId(1), CustomerId(10), 4, "No wobble on an old uneven floor, which surprised me.", At(2026, 3, 5, 7, 55)),

        // Product 2, Larsen Dining Chair.
        new(ReviewId(13), ProductId(2), CustomerId(1), 4, "Bought four. They stack, which is the whole reason I bought them.", At(2025, 4, 28, 13, 7)),
        new(ReviewId(14), ProductId(2), CustomerId(5), 5, "The paper cord seat is more comfortable than it looks.", At(2025, 6, 14, 17, 29)),
        new(ReviewId(15), ProductId(2), CustomerId(9), 3, "Fine chair, but the cord seat picks up crumbs and I have children.", At(2025, 7, 22, 8, 16)),
        new(ReviewId(16), ProductId(2), CustomerId(3), 4, "Matches the table exactly, which I half expected it not to.", At(2025, 8, 30, 15, 44)),
        new(ReviewId(17), ProductId(2), CustomerId(11), 4, null, At(2025, 10, 4, 10, 31)),
        new(ReviewId(18), ProductId(2), CustomerId(7), 5, "Light enough that I can move all six, one in each hand.", At(2025, 12, 11, 18, 3)),
        new(ReviewId(19), ProductId(2), CustomerId(2), 2, "One of the two I ordered creaks. The other is perfect. Quality control is a lottery.", At(2026, 2, 7, 9, 49)),
        new(ReviewId(20), ProductId(2), CustomerId(12), 4, "Eight months of daily use and the seat has not sagged.", At(2026, 4, 23, 11, 12)),

        // Product 3, Brant Two-Seat Sofa.
        new(ReviewId(21), ProductId(3), CustomerId(6), 5, "The feather cushions need plumping every couple of days. Still worth it.", At(2025, 5, 9, 20, 37)),
        new(ReviewId(22), ProductId(3), CustomerId(10), 4, "Firmer than the showroom model, which suits me.", At(2025, 6, 25, 14, 18)),
        new(ReviewId(23), ProductId(3), CustomerId(4), 4, "It does a two seat sofa's job: two people, no more.", At(2025, 7, 30, 19, 51)),
        new(ReviewId(24), ProductId(3), CustomerId(8), 5, "Covers come off for washing without a fight.", At(2025, 9, 14, 9, 26)),
        new(ReviewId(25), ProductId(3), CustomerId(1), 3, "Comfortable, but the beech frame is audible when you sit down heavily.", At(2025, 10, 28, 16, 9)),
        new(ReviewId(26), ProductId(3), CustomerId(12), 5, "Bought the matching ottoman at the same time. Both good.", At(2025, 11, 19, 8, 47)),
        new(ReviewId(27), ProductId(3), CustomerId(5), 4, "Delivery team took the old one away. Small thing, made the day.", At(2026, 1, 8, 13, 35)),
        new(ReviewId(28), ProductId(3), CustomerId(9), 1, "Arrived with the wrong cover colour and the exchange took five weeks.", At(2026, 2, 21, 10, 4)),
        new(ReviewId(29), ProductId(3), CustomerId(3), 4, "Deep enough to nap on if you are under six foot.", At(2026, 4, 11, 21, 22)),
        new(ReviewId(30), ProductId(3), CustomerId(7), 5, "Nine months in and it still looks like the photo.", At(2026, 6, 2, 12, 50)),

        // Product 4, Brant Ottoman.
        new(ReviewId(31), ProductId(4), CustomerId(2), 4, "Doubles as a coffee table with a tray on top.", At(2025, 8, 17, 17, 13)),
        new(ReviewId(32), ProductId(4), CustomerId(11), 5, "Cover zips off. Ours has been washed twice already.", At(2026, 1, 30, 9, 41)),
        new(ReviewId(33), ProductId(4), CustomerId(6), 3, "Slightly taller than the sofa seat, so it does not line up flush.", At(2026, 5, 14, 15, 28)),

        // Product 5, Kessel Writing Desk.
        new(ReviewId(34), ProductId(5), CustomerId(8), 5, "The cable channel is the reason I bought it and it does the job.", At(2025, 5, 26, 8, 9)),
        new(ReviewId(35), ProductId(5), CustomerId(4), 4, "Narrow, as advertised. Measure your laptop first.", At(2025, 7, 11, 11, 47)),
        new(ReviewId(36), ProductId(5), CustomerId(10), 4, null, At(2025, 9, 23, 19, 2)),
        new(ReviewId(37), ProductId(5), CustomerId(1), 2, "Drawer runs rough. Wax helped, but I should not have had to.", At(2025, 12, 2, 14, 55)),
        new(ReviewId(38), ProductId(5), CustomerId(7), 5, "Perfect for a hallway that was doing nothing.", At(2026, 3, 18, 10, 16)),
        new(ReviewId(39), ProductId(5), CustomerId(12), 4, "Sturdy enough that I do not feel it when I type.", At(2026, 6, 27, 16, 38)),

        // Product 6, Halden Bed Frame.
        new(ReviewId(40), ProductId(6), CustomerId(3), 5, "No box spring, no squeak, no complaints.", At(2025, 6, 5, 21, 4)),
        new(ReviewId(41), ProductId(6), CustomerId(9), 5, "Slats are thick. I have broken beds before and this one feels different.", At(2025, 8, 24, 9, 33)),
        new(ReviewId(42), ProductId(6), CustomerId(5), 4, "Assembly is a two person job whatever the instructions say.", At(2025, 11, 8, 18, 26)),
        new(ReviewId(43), ProductId(6), CustomerId(11), 3, "Low to the ground. My knees noticed.", At(2026, 2, 14, 7, 48)),
        new(ReviewId(44), ProductId(6), CustomerId(2), 5, "Six months, no creaks.", At(2026, 5, 30, 12, 7)),

        // Product 7, Orbit Pendant Lamp.
        new(ReviewId(45), ProductId(7), CustomerId(1), 5, "The opal glass throws a soft light that does not glare off the table.", At(2025, 4, 19, 20, 52)),
        new(ReviewId(46), ProductId(7), CustomerId(6), 4, "Brass stem is genuinely brushed, not painted.", At(2025, 6, 18, 15, 31)),
        new(ReviewId(47), ProductId(7), CustomerId(10), 5, "Hung it over the island. Height was right first try.", At(2025, 8, 2, 10, 44)),
        new(ReviewId(48), ProductId(7), CustomerId(4), 4, "Bulb not included. Minor, but check before you order.", At(2025, 9, 29, 17, 19)),
        new(ReviewId(49), ProductId(7), CustomerId(8), 5, "Second one bought for the hall. That should say enough.", At(2025, 11, 14, 8, 36)),
        new(ReviewId(50), ProductId(7), CustomerId(12), 3, "The fitting kit assumes a ceiling rose. Mine did not have one.", At(2026, 1, 25, 13, 58)),
        new(ReviewId(51), ProductId(7), CustomerId(7), 4, "Glass wipes clean. Dust shows quickly on the brass.", At(2026, 4, 6, 9, 23)),
        new(ReviewId(52), ProductId(7), CustomerId(3), 5, "Warm and even. Nothing else in the room needed changing.", At(2026, 7, 1, 19, 41)),

        // Product 8, Orbit Floor Lamp.
        new(ReviewId(53), ProductId(8), CustomerId(5), 4, "Base is heavy enough that the cat has given up.", At(2025, 7, 6, 16, 12)),
        new(ReviewId(54), ProductId(8), CustomerId(9), 5, "Same light as the pendant without drilling the ceiling.", At(2025, 10, 13, 20, 29)),
        new(ReviewId(55), ProductId(8), CustomerId(2), 4, "Cable is shorter than I expected. Check your socket.", At(2026, 2, 28, 11, 34)),
        new(ReviewId(56), ProductId(8), CustomerId(11), 3, null, At(2026, 6, 15, 8, 51)),

        // Product 9, Fen Reading Light.
        new(ReviewId(57), ProductId(9), CustomerId(7), 5, "Clamps onto a headboard up to about four centimetres thick.", At(2025, 5, 15, 22, 7)),
        new(ReviewId(58), ProductId(9), CustomerId(1), 4, "Dimmer goes low enough to read next to someone asleep.", At(2025, 7, 28, 21, 45)),
        new(ReviewId(59), ProductId(9), CustomerId(12), 5, "Bought two. Ended a long running argument about bedside lamps.", At(2025, 9, 9, 18, 14)),
        new(ReviewId(60), ProductId(9), CustomerId(6), 5, "Second one for the other side of the bed.", At(2025, 12, 20, 10, 27)),
        new(ReviewId(61), ProductId(9), CustomerId(4), 2, "The clamp scratched my desk edge within a week. Use a cloth.", At(2026, 3, 2, 14, 3)),
        new(ReviewId(62), ProductId(9), CustomerId(10), 4, "Arm holds position. That is rarer than it should be.", At(2026, 5, 19, 9, 38)),
        new(ReviewId(63), ProductId(9), CustomerId(8), 5, "Warm bulb included, which I did not expect at this price.", At(2026, 7, 12, 17, 56)),

        // Product 10, Fen Wall Sconce, has no reviews: it is hardwired, so most
        // buyers are fitters rather than the customers who leave comments.

        // Product 11, Tallow Candle Set.
        new(ReviewId(64), ProductId(11), CustomerId(3), 3, "Burn evenly, but the scent is faint. Fine if that is what you want.", At(2025, 10, 31, 19, 15)),
        new(ReviewId(65), ProductId(11), CustomerId(9), 5, "Eight hours a candle, measured. No smoke.", At(2026, 4, 30, 20, 44)),

        // Product 12, Ridge Chef's Knife.
        new(ReviewId(66), ProductId(12), CustomerId(2), 5, "Takes an edge quickly and holds it for weeks of home cooking.", At(2025, 4, 8, 12, 19)),
        new(ReviewId(67), ProductId(12), CustomerId(11), 5, "Walnut handle fits my hand better than the plastic one it replaced.", At(2025, 6, 1, 15, 52)),
        new(ReviewId(68), ProductId(12), CustomerId(5), 4, "Carbon steel means drying it every time. Know that going in.", At(2025, 7, 19, 9, 7)),
        new(ReviewId(69), ProductId(12), CustomerId(7), 5, "Best knife in the drawer within a day of arriving.", At(2025, 9, 5, 18, 33)),
        new(ReviewId(70), ProductId(12), CustomerId(12), 3, "Mine arrived less sharp than I hoped. Ten minutes on a stone fixed it.", At(2025, 11, 2, 11, 26)),
        new(ReviewId(71), ProductId(12), CustomerId(1), 5, null, At(2026, 1, 12, 16, 41)),
        new(ReviewId(72), ProductId(12), CustomerId(8), 4, "Patina came on fast. I like it, my partner does not.", At(2026, 3, 27, 8, 58)),
        new(ReviewId(73), ProductId(12), CustomerId(4), 5, "Twenty centimetres is the right size for one cook in a small kitchen.", At(2026, 5, 8, 13, 12)),
        new(ReviewId(74), ProductId(12), CustomerId(10), 2, "Spotted rust after two weeks. My fault for leaving it wet, but a warning on the box would have helped.", At(2026, 7, 20, 10, 35)),

        // Product 13, Ridge Paring Knife.
        new(ReviewId(75), ProductId(13), CustomerId(6), 5, "Does the fiddly work the big knife is clumsy at.", At(2025, 8, 13, 14, 22)),
        new(ReviewId(76), ProductId(13), CustomerId(3), 4, "Same steel, same care routine, smaller price.", At(2025, 10, 25, 9, 49)),
        new(ReviewId(77), ProductId(13), CustomerId(9), 4, "Sharper out of the box than the chef's knife was.", At(2026, 1, 19, 17, 6)),
        new(ReviewId(78), ProductId(13), CustomerId(11), 5, "Peels and trims all day without tiring my wrist.", At(2026, 4, 14, 11, 29)),
        new(ReviewId(79), ProductId(13), CustomerId(5), 3, "Handle is a fraction too short for my hand.", At(2026, 6, 22, 19, 47)),

        // Product 14, Copper Saute Pan.
        new(ReviewId(80), ProductId(14), CustomerId(8), 5, "Heats fast and evenly. The tin lining wants a gentle hand.", At(2025, 9, 18, 12, 36)),
        new(ReviewId(81), ProductId(14), CustomerId(2), 4, "Iron handle gets hot on the hob. Keep a cloth nearby.", At(2026, 2, 3, 18, 21)),
        new(ReviewId(82), ProductId(14), CustomerId(12), 3, "Heavy. Lovely, but heavy.", At(2026, 6, 8, 10, 53)),

        // Product 15, Stoneware Mixing Bowl.
        new(ReviewId(83), ProductId(15), CustomerId(4), 4, "The pouring lip actually pours. Low bar, cleared.", At(2025, 7, 2, 8, 44)),
        new(ReviewId(84), ProductId(15), CustomerId(10), 5, "Three litres is enough for a double batch of bread dough.", At(2025, 11, 21, 15, 17)),
        new(ReviewId(85), ProductId(15), CustomerId(1), 4, null, At(2026, 3, 11, 9, 2)),
        new(ReviewId(86), ProductId(15), CustomerId(7), 4, "Heavy enough to stay put while you whisk.", At(2026, 7, 5, 16, 29)),

        // Product 16, Beech Cutting Board.
        new(ReviewId(87), ProductId(16), CustomerId(9), 5, "End grain, so it is kind to the knife edge.", At(2025, 5, 31, 13, 48)),
        new(ReviewId(88), ProductId(16), CustomerId(6), 4, "Rubber feet keep it still. Oil it monthly and it stays sealed.", At(2025, 8, 20, 17, 35)),
        new(ReviewId(89), ProductId(16), CustomerId(12), 5, "Big enough to break down a chicken without spilling over the edge.", At(2025, 10, 9, 10, 12)),
        new(ReviewId(90), ProductId(16), CustomerId(3), 4, "Thick and heavy. It lives on the counter now.", At(2026, 1, 5, 19, 26)),
        new(ReviewId(91), ProductId(16), CustomerId(5), 2, "A hairline split opened along one glue line after four months.", At(2026, 4, 19, 8, 41)),
        new(ReviewId(92), ProductId(16), CustomerId(11), 5, "Heavier than the board it replaced, and better in every way.", At(2026, 7, 16, 14, 58)),

        // Product 17, Enamel Kettle, has no reviews: it sold out on the first day
        // it was listed and nobody has taken delivery of one yet.

        // Product 18, Marle Wool Throw.
        new(ReviewId(93), ProductId(18), CustomerId(2), 5, "Warm without weighing anything. Lives on the end of the sofa.", At(2025, 10, 16, 20, 23)),
        new(ReviewId(94), ProductId(18), CustomerId(8), 5, "The herringbone looks better in person than in the photograph.", At(2025, 12, 5, 18, 47)),
        new(ReviewId(95), ProductId(18), CustomerId(10), 4, "Sheds a little for the first fortnight, then stops.", At(2026, 1, 22, 9, 14)),
        new(ReviewId(96), ProductId(18), CustomerId(4), 3, "Softer wools exist at this price. This one is scratchier than I wanted.", At(2026, 3, 9, 16, 32)),
        new(ReviewId(97), ProductId(18), CustomerId(7), 5, null, At(2026, 5, 25, 11, 8)),
        new(ReviewId(98), ProductId(18), CustomerId(1), 4, "Big enough for two people if neither of you pulls.", At(2026, 7, 27, 19, 53)),

        // Product 19, Marle Cushion Cover.
        new(ReviewId(99), ProductId(19), CustomerId(11), 4, "Cover only, no insert. That is stated, but people miss it.", At(2025, 11, 30, 10, 37)),
        new(ReviewId(100), ProductId(19), CustomerId(6), 5, "Zip is hidden and the weave matches the throw exactly.", At(2026, 2, 17, 15, 4)),
        new(ReviewId(101), ProductId(19), CustomerId(9), 3, "Came out of the wash a size smaller. Read the label, unlike me.", At(2026, 6, 11, 8, 28)),

        // Product 20, Linen Sheet Set.
        new(ReviewId(102), ProductId(20), CustomerId(5), 5, "Rough for a week, then softer than any cotton set we have owned.", At(2025, 4, 24, 21, 11)),
        new(ReviewId(103), ProductId(20), CustomerId(12), 4, "Wrinkled out of the bag. That is linen, not a fault.", At(2025, 6, 21, 9, 46)),
        new(ReviewId(104), ProductId(20), CustomerId(3), 5, "Cool in August, warm in January. I do not understand it either.", At(2025, 8, 27, 22, 34)),
        new(ReviewId(105), ProductId(20), CustomerId(8), 5, "Ordered the double. Fits with room to tuck.", At(2025, 10, 22, 7, 59)),
        new(ReviewId(106), ProductId(20), CustomerId(10), 4, "Fitted sheet has deep corners. Fits a thick mattress.", At(2026, 1, 27, 13, 21)),
        new(ReviewId(107), ProductId(20), CustomerId(2), 2, "A seam gave way at three months. Replaced without argument, but still.", At(2026, 3, 24, 17, 48)),
        new(ReviewId(108), ProductId(20), CustomerId(7), 5, "Third set bought. The first two are still in rotation.", At(2026, 5, 6, 10, 15)),
        new(ReviewId(109), ProductId(20), CustomerId(4), 4, "Colour is closer to oatmeal than the photograph suggests.", At(2026, 7, 9, 18, 36)),

        // Product 21, Waffle Bath Towel.
        new(ReviewId(110), ProductId(21), CustomerId(1), 4, "Dries overnight on a hook, which the old terry ones never did.", At(2026, 2, 11, 8, 3)),
        new(ReviewId(111), ProductId(21), CustomerId(9), 5, "Thin, which sounds bad and is not. Absorbs plenty.", At(2026, 6, 30, 12, 27)),

        // Product 22, Jute Floor Runner, has no reviews: it has been out of stock
        // since the week it launched.

        // Product 23, Corbel Shelf Unit.
        new(ReviewId(112), ProductId(23), CustomerId(6), 5, "Uprights are properly rigid once the shelves go in.", At(2025, 9, 11, 14, 41)),
        new(ReviewId(113), ProductId(23), CustomerId(11), 4, "Holds books without bowing. That was the test.", At(2025, 12, 16, 11, 55)),
        new(ReviewId(114), ProductId(23), CustomerId(12), 4, null, At(2026, 4, 2, 9, 19)),
        new(ReviewId(115), ProductId(23), CustomerId(5), 3, "Fixings for a plasterboard wall are not included and should be.", At(2026, 7, 23, 16, 44)),

        // Product 24, Corbel Wall Shelf.
        new(ReviewId(116), ProductId(24), CustomerId(10), 4, "Ninety centimetres and it does not sag in the middle.", At(2026, 5, 12, 10, 58)),

        // Product 25, Canvas Storage Bin.
        new(ReviewId(117), ProductId(25), CustomerId(8), 4, "Collapses flat when the toys are out of it, which is never.", At(2025, 7, 25, 15, 23)),
        new(ReviewId(118), ProductId(25), CustomerId(2), 3, "The leather handles are the only part that feels cheap.", At(2025, 12, 29, 18, 12)),
        new(ReviewId(119), ProductId(25), CustomerId(7), 5, "Stands up on its own even when empty.", At(2026, 3, 31, 9, 35)),
        new(ReviewId(120), ProductId(25), CustomerId(3), 4, null, At(2026, 6, 18, 17, 2))
    ];
}
