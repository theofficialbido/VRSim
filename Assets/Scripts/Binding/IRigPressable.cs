namespace VRSIM.Binding
{
    /// <summary>
    /// Anything in the scene that can be activated by the player.
    ///
    /// Exists so that input mechanisms and controls do not know about each
    /// other. A ray pointer, a proximity trigger, an XRI interactable or a UI
    /// button can all drive any control, and a new control type needs no change
    /// to any of them.
    /// </summary>
    public interface IRigPressable
    {
        void Press();

        /// <summary>Short text for a pointer or label to show. May be null.</summary>
        string PressableLabel { get; }
    }
}
