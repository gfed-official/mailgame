using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Net;

namespace PerformativeMail.App
{
    /// <summary>
    /// Hot reload in the smoke's first Prep (doc 07 §6, what F6 does in a debug build): the host's
    /// content read again through Godot's files with one number changed takes at once — the
    /// server walks at the new speed, the door wants the new content — a broken file is turned
    /// away with a line in the feed and changes nothing, and the files as they are bring it all back.
    /// </summary>
    public partial class SmokeDriver
    {
        private const float ReloadWalkSpeed = 6.5f;

        /// <summary>res://content with some files' text changed.</summary>
        private sealed class EditedContentSource : IContentSource
        {
            private readonly IContentSource _inner;
            private readonly string _file, _from, _to;

            public EditedContentSource(IContentSource inner, string file, string from, string to)
            {
                _inner = inner;
                _file = file;
                _from = from;
                _to = to;
            }

            public string? ReadText(string relativePath)
            {
                string? text = _inner.ReadText(relativePath);
                return text != null && relativePath == _file ? text.Replace(_from, _to) : text;
            }

            public IReadOnlyList<string> ListJson(string relativeDir) => _inner.ListJson(relativeDir);
        }

        private void CheckHotReload()
        {
            var server = _session.Server!;
            var content = _session.Content;
            var files = new GodotContentSource();
            float walk = server.World.MoveConstants.WalkSpeed;
            uint hash = server.ProtocolHash;
            string from = "\"walkSpeed\": " + walk.ToString("0.0", CultureInfo.InvariantCulture);
            Check(files.ReadText("balance.json")?.Contains(from, StringComparison.Ordinal) == true, $"balance.json has no {from} to change");

            Check(_session.HotReload(new EditedContentSource(files, "balance.json", from, "\"walkSpeed\": " + ReloadWalkSpeed.ToString("0.0", CultureInfo.InvariantCulture))), "the hot reload of a changed walk speed was refused");
            Check(server.World.MoveConstants.WalkSpeed == ReloadWalkSpeed && content.Balance.GetFloat("walkSpeed") == ReloadWalkSpeed,
                $"after the hot reload the server walks at {server.World.MoveConstants.WalkSpeed}");
            Check(server.ProtocolHash != hash && server.ProtocolHash == Protocol.Hash(content), "after the hot reload the server still lets in the old content");
            Check(_session.Feed.Entries.Any(e => e.Text == HudText.HotReloaded(content) && !e.Bad), "the feed does not say the content was reloaded");

            Check(!_session.HotReload(new EditedContentSource(files, "balance.json", from, "\"walkSpeed\": \"fast\"")), "a broken balance.json was taken");
            Check(server.World.MoveConstants.WalkSpeed == ReloadWalkSpeed, "a refused hot reload changed the walk speed");
            Check(_session.Feed.Entries.Any(e => e.Bad && e.Text.StartsWith("Content not reloaded: balance.json", StringComparison.Ordinal)), "the feed does not say the broken content was refused");

            Check(_session.HotReload(files), "the hot reload of the files as they are was refused");
            Check(server.World.MoveConstants.WalkSpeed == walk && server.ProtocolHash == hash, $"the files as they are left a walk speed of {server.World.MoveConstants.WalkSpeed}, hash {server.ProtocolHash:x8} (was {hash:x8})");
            GD.Print($"[smoke] hot reload: walk speed {walk} -> {ReloadWalkSpeed} and back through res://content, a broken balance.json turned away");
        }
    }
}
