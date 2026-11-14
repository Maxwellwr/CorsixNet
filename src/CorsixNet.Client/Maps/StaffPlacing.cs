namespace CorsixNet.Client.Maps;

/// <summary>What a frame's clicks do to a member of staff being hired or carried.</summary>
internal enum StaffPlacingClick
{
    /// <summary>No click: they stay on the cursor.</summary>
    None,

    /// <summary>A left click: put them down on the tile under the cursor, if they may stand there.</summary>
    Place,

    /// <summary>A right click, or the candidate is no longer for hire: they leave the cursor.</summary>
    Cancel,

    /// <summary>A click while the game is paused: nothing happens and they stay on the cursor.</summary>
    Paused,
}

/// <summary>
/// The clicks of <c>UIPlaceStaff:onMouseUp</c> (CorsixTH/Lua/dialogs/place_staff.lua), which does nothing unless
/// <c>world.user_actions_allowed</c>: while the game is paused (and <c>allow_user_actions_while_paused</c> is off, its
/// default) neither button puts the member of staff down nor takes them off the cursor. Here a hire put down while paused
/// would only be carried out when the game goes on, so the candidate vanished from the cursor without appearing.
/// </summary>
internal static class StaffPlacing
{
    public static StaffPlacingClick Click(bool candidateGone, bool left, bool right, bool paused)
    {
        if (candidateGone)
        {
            return StaffPlacingClick.Cancel;
        }

        if (!left && !right)
        {
            return StaffPlacingClick.None;
        }

        if (paused)
        {
            return StaffPlacingClick.Paused;
        }

        return right ? StaffPlacingClick.Cancel : StaffPlacingClick.Place;
    }
}
