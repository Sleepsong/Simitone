/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.SimAntics;

namespace Simitone.Client.Packs
{
    /// <summary>
    /// The entry point of a pack's code layer. Simitone creates one instance of every public class in the pack's
    /// assembly that implements this and has a public constructor without parameters, then calls it at these points.
    /// Implement only the ones you need. If any of them throws, the pack's code gets no more calls for the rest of the run.
    /// </summary>
    public interface ISimitonePack
    {
        /// <summary>
        /// Once at startup, on the game thread, before any game content is loaded.
        /// Console commands (PackManager.RegisterCommand) can be added here.
        /// </summary>
        void OnLoad(Pack pack) { }

        /// <summary>
        /// Once, when the game's content and the SimAntics primitives are loaded. This is the place to add primitives
        /// (PackManager.RegisterPrimitive) and other engine extensions. Called on the loading thread, not the game thread.
        /// </summary>
        void OnContentReady() { }

        /// <summary>
        /// Each time a lot has been loaded and is about to be played, on the game thread.
        /// </summary>
        void OnLotLoaded(VM vm) { }
    }
}
