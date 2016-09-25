/******************************************************************************
 * Spine Runtimes Software License
 * Version 2.3
 * 
 * Copyright (c) 2013-2015, Esoteric Software
 * All rights reserved.
 * 
 * You are granted a perpetual, non-exclusive, non-sublicensable and
 * non-transferable license to use, install, execute and perform the Spine
 * Runtimes Software (the "Software") and derivative works solely for personal
 * or internal use. Without the written permission of Esoteric Software (see
 * Section 2 of the Spine Software License Agreement), you may not (a) modify,
 * translate, adapt or otherwise create derivative works, improvements of the
 * Software or develop new applications using the Software or (b) remove,
 * delete, alter or obscure any trademarks or any copyright, trademark, patent
 * or other intellectual property or proprietary rights notices on or in the
 * Software, including any copy thereof. Redistributions in binary or source
 * form must include this license and terms.
 * 
 * THIS SOFTWARE IS PROVIDED BY ESOTERIC SOFTWARE "AS IS" AND ANY EXPRESS OR
 * IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO
 * EVENT SHALL ESOTERIC SOFTWARE BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
 * OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR
 * OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using System;
using System.Collections.Generic;
using System.Text;

namespace Spine {
	public class AnimationState {
		static Animation EmptyAnimation = new Animation("<empty>", new ExposedList<Timeline>(), 0);

		private AnimationStateData data;
		private readonly ExposedList<TrackEntry> tracks = new ExposedList<TrackEntry>();
		private readonly HashSet<int> propertyIDs = new HashSet<int>();
		private readonly ExposedList<Event> events = new ExposedList<Event>();
		private readonly EventQueue queue;
		bool animationsChanged;
		private float timeScale = 1;

		public AnimationStateData Data { get { return data; } }
		/// <summary>A list of tracks that have animations, which may contain nulls.</summary>
		public ExposedList<TrackEntry> Tracks { get { return tracks; } }
		public float TimeScale { get { return timeScale; } set { timeScale = value; } }

		public delegate void TrackEntryDelegate (TrackEntry trackEntry);
		public event TrackEntryDelegate Start, Interrupt, End, Dispose, Complete;

		public delegate void TrackEntryEventDelegate (TrackEntry trackEntry, Event e);
		public event TrackEntryEventDelegate Event;

		public AnimationState (AnimationStateData data) {
			if (data == null) throw new ArgumentNullException("data", "data cannot be null.");
			this.data = data;
			this.queue = new EventQueue(this, HandleAnimationsChanged);
		}

		//readonly List<Animation> animationsToRemove = new List<Animation>();

		void HandleAnimationsChanged () {
			this.animationsChanged = true;
		}

		/// <summary>
		/// Increments the track entry times, setting queued animations as current if needed</summary>
		/// <param name="delta">delta time</param>
		public void Update (float delta) {
			delta *= timeScale;
			var tracksItems = tracks.Items;
			for (int i = 0, n = tracks.Count; i < n; i++) {
				TrackEntry current = tracksItems[i];
				if (current == null) continue;

				current.animationLast = current.nextAnimationLast;
				current.trackLast = current.nextTrackLast;

				float currentDelta = delta * current.timeScale;

				if (current.delay > 0) {
					current.delay -= currentDelta;
					if (current.delay > 0) continue;
					currentDelta = -current.delay;
					current.delay = 0;
				}

				TrackEntry next = current.next;
				if (next != null) {
					// When the next entry's delay is passed, change to the next entry.
					float nextTime = current.trackLast - next.delay;
					if (nextTime == 0) {
						next.delay = 0;
						next.trackTime = nextTime + (delta * next.timeScale);
						current.trackTime += currentDelta;
						SetCurrent(i, next);
						if (next.mixingFrom != null) next.mixTime += currentDelta;
						continue;
					}
					UpdateMixingFrom(current, delta);
				} else {
					UpdateMixingFrom(current, delta);
					// Clear the track when the track end time is reached and there is no next entry.
					//UnityEngine.Debug.Log(current.animation.name + " is ending.");
					if (current.trackLast >= current.trackEnd && current.mixingFrom == null) {
						tracksItems[i] = null;
						queue.End(current);
						DisposeNext(current);
						continue;
					}
				}

				current.trackTime += currentDelta;
			}

			queue.Drain();
		}

		private void UpdateMixingFrom (TrackEntry entry, float delta) {
			TrackEntry from = entry.mixingFrom;
			if (from == null) return;

			if (entry.mixTime >= entry.mixDuration && entry.mixTime > 0) {
				queue.End(from);
				TrackEntry newFrom = from.mixingFrom;
				entry.mixingFrom = newFrom;
				if (newFrom == null) return;
				entry.mixTime = from.mixTime;
				entry.mixDuration = from.mixDuration;
				from = newFrom;
			}

			from.animationLast = from.nextAnimationLast;
			from.trackLast = from.nextTrackLast;
			float mixingFromDelta = delta * from.timeScale;
			from.trackTime += mixingFromDelta;
			entry.mixTime += mixingFromDelta;

			UpdateMixingFrom(from, delta);
		}
			

		/// <summary>
		/// Poses the skeleton using the track entry animations. There are no side effects other than invoking listeners, so the 
		/// animation state can be applied to multiple skeletons to pose them identically.</summary>
		public void Apply (Skeleton skeleton) {
			if (animationsChanged) AnimationsChanged();

//			if (animationsToRemove.Count != 0) {
//				foreach (var a in animationsToRemove)
//					a.SetKeyedItemsToSetupPose(skeleton);
//			}

			var events = this.events;

			var tracksItems = tracks.Items;
			for (int i = 0; i < tracks.Count; i++) {
				TrackEntry current = tracksItems[i];
				if (current == null) continue;
				if (current.delay > 0) continue;

				// Apply mixing from entries first.
				float mix = current.alpha;
				if (current.mixingFrom != null) mix = ApplyMixingFrom(current, skeleton, mix);

				// Apply current entry.
				float animationLast = current.animationLast, animationTime = current.AnimationTime;
				var timelines = current.animation.timelines;
				var timelinesItems = timelines.Items;
				if (mix == 1) {
					for (int ii = 0, n = timelines.Count; ii < n; ii++)
						timelinesItems[ii].Apply(skeleton, animationLast, animationTime, events, 1, false, false);
				} else {
					bool firstFrame = current.timelinesRotation.Count == 0;
					if (firstFrame) current.timelinesRotation.EnsureCapacity(timelines.Count << 1);
					var timelinesRotation = current.timelinesRotation.Items;
					var timelinesFirst = current.timelinesFirst;
					for (int ii = 0, n = timelines.Count; ii < n; ii++) {
						Timeline timeline = timelinesItems[ii];
						var rotateTimeline = timeline as RotateTimeline;
						if (rotateTimeline != null) {
							ApplyRotateTimeline(rotateTimeline, skeleton, animationLast, animationTime, events, mix,
								timelinesFirst.Items[ii], false, timelinesRotation, ii << 1, firstFrame);
						} else {
							timeline.Apply(skeleton, animationLast, animationTime, events, mix, timelinesFirst.Items[ii], false);
						}
					}
				}
				QueueEvents(current, animationTime);
				current.nextAnimationLast = animationTime;
				current.nextTrackLast = current.trackTime;
			}

			queue.Drain();
		}

		private float ApplyMixingFrom (TrackEntry entry, Skeleton skeleton, float alpha) {
			float mix;
			if (entry.mixDuration == 0) // Single frame mix to undo mixingFrom changes.
				mix = 1;
			else {
				mix = alpha * entry.mixTime / entry.mixDuration;
				if (mix > 1) mix = 1;
			}

			TrackEntry from = entry.mixingFrom;
			if (from.mixingFrom != null) ApplyMixingFrom(from, skeleton, alpha);

			var eventBuffer = mix < entry.eventThreshold ? this.events : null;
			bool attachments = mix < entry.attachmentThreshold, drawOrder = mix < entry.drawOrderThreshold;

			float animationLast = entry.animationLast, animationTime = entry.AnimationTime;
			var timelines = entry.animation.timelines;
			int timelineCount = timelines.Count;
			var timelinesFirst = entry.timelinesFirst;
			var timelinesLast = entry.timelinesLast;
			float alphaFull = entry.alpha, alphaMix = alphaFull * (1 - mix);

			bool firstFrame = entry.timelinesRotation.Count == 0;
			if (firstFrame) entry.timelinesRotation.Capacity = timelineCount << 1;
			var timelinesRotation = entry.timelinesRotation.Items;

			for (int i = 0; i < timelineCount; i++) {
				Timeline timeline = timelines.Items[i];
				bool setupPose = timelinesFirst.Items[i];
				float a = timelinesLast.Items[i] ? alphaMix : alphaFull;
				var rotateTimeline = timeline as RotateTimeline;
				if (rotateTimeline != null && alpha < 1) {
					ApplyRotateTimeline(rotateTimeline, skeleton, animationLast, animationTime, eventBuffer, a, setupPose,
						setupPose, timelinesRotation, i << 1, firstFrame);
				} else {
					if (setupPose) {
						if (!attachments && timeline is AttachmentTimeline) continue;
						if (!drawOrder && timeline is DrawOrderTimeline) continue;
					}
					timeline.Apply(skeleton, animationLast, animationTime, eventBuffer, a, setupPose, setupPose);
				}
			}

			QueueEvents(entry, animationTime);
			entry.nextAnimationLast = animationTime;
			entry.nextTrackLast = entry.trackTime;

			return mix;
		}

		/// <param name="events">May be null.</param>
		static private void ApplyRotateTimeline (RotateTimeline timeline, Skeleton skeleton, float lastTime, float time, ExposedList<Event> events,
			float alpha, bool setupPose, bool mixingOut, float[] timelinesRotation, int i, bool firstFrame) {
			if (alpha == 1) {
				timeline.Apply(skeleton, lastTime, time, events, 1, setupPose, setupPose);
				return;
			}

			float[] frames = timeline.frames;
			if (time < frames[0]) return; // Time is before first frame.

			Bone bone = skeleton.bones.Items[timeline.boneIndex];

			float r2;
			if (time >= frames[frames.Length - RotateTimeline.ENTRIES]) // Time is after last frame.
				r2 = bone.data.rotation + frames[frames.Length + RotateTimeline.PREV_ROTATION];
			else {
				// Interpolate between the previous frame and the current frame.
				int frame = Animation.BinarySearch(frames, time, RotateTimeline.ENTRIES);
				float prevRotation = frames[frame + RotateTimeline.PREV_ROTATION];
				float frameTime = frames[frame];
				float percent = timeline.GetCurvePercent((frame >> 1) - 1,
					1 - (time - frameTime) / (frames[frame + RotateTimeline.PREV_TIME] - frameTime));

				r2 = frames[frame + RotateTimeline.ROTATION] - prevRotation;
				r2 -= (16384 - (int)(16384.499999999996 - r2 / 360)) * 360;
				r2 = prevRotation + r2 * percent + bone.data.rotation;
				r2 -= (16384 - (int)(16384.499999999996 - r2 / 360)) * 360;
			}

			// Mix between two rotations using the direction of the shortest route on the first frame while detecting crosses.
			float r1 = setupPose ? bone.data.rotation : bone.rotation;
			float total, diff = r2 - r1;
			if (diff == 0) {
				if (firstFrame) {
					timelinesRotation[i] = 0;
					total = 0;
				} else
					total = timelinesRotation[i];
			} else {
				diff -= (16384 - (int)(16384.499999999996 - diff / 360)) * 360;
				float lastTotal, lastDiff;
				if (firstFrame) {
					lastTotal = 0;
					lastDiff = diff;
				} else {
					lastTotal = timelinesRotation[i]; // Angle and direction of mix, including loops.
					lastDiff = timelinesRotation[i + 1]; // Difference between bones.
				}
				bool current = diff > 0, dir = lastTotal >= 0;
				// Detect cross at 0 (not 180).
				if (Math.Sign(lastDiff) != Math.Sign(diff) && Math.Abs(lastDiff) <= 90) {
					// A cross after a 360 rotation is a loop.
					if (Math.Abs(lastTotal) > 180) lastTotal += 360 * Math.Sign(lastTotal);
					dir = current;
				}
				total = diff + lastTotal - lastTotal % 360; // Keep loops part of lastTotal.
				if (dir != current) total += 360 * Math.Sign(lastTotal);
				timelinesRotation[i] = total;
			}
			timelinesRotation[i + 1] = diff;
			r1 += total * alpha;
			bone.rotation = r1 - (16384 - (int)(16384.499999999996 - r1 / 360)) * 360;
		}

		private void QueueEvents (TrackEntry entry, float animationTime) {
			float animationStart = entry.animationStart, animationEnd = entry.animationEnd;
			float duration = animationEnd - animationStart;
			float trackLastWrapped = entry.trackLast % duration;

			// Queue events before complete.
			var events = this.events;
			var eventsItems = events.Items;
			int i = 0, n = events.Count;
			for (; i < n; i++) {
				var e = eventsItems[i];
				if (e.time < trackLastWrapped) break;
				if (e.time > animationEnd) continue; // Discard events outside animation start/end.
				queue.Event(entry, e);
			}

			// Queue complete if completed a loop iteration or the animation.
			if (entry.loop ? (trackLastWrapped > entry.trackTime % duration)
				: (animationTime >= animationEnd && entry.animationLast < animationEnd)) {
				queue.Complete(entry);
			}

			// Queue events after complete.
			for (; i < n; i++) {
				Event e = eventsItems[i];
				if (e.time < animationStart) continue; // Discard events outside animation start/end.
				queue.Event(entry, eventsItems[i]);
			}
			events.Clear(false);
		}

		/// <summary>
		/// Removes all animations from all tracks, leaving skeletons in their last pose. 
		/// It may be desired to use <see cref="AnimationState.SetEmptyAnimations(float)"/> to mix the skeletons back to the setup pose, 
		/// rather than leaving them in their last pose.
		/// </summary>
		public void ClearTracks () {
			queue.drainDisabled = true;
			for (int i = 0, n = tracks.Count; i < n; i++) {
				ClearTrack(i);
			}
			tracks.Clear();
			queue.drainDisabled = false;
			queue.Drain();
		}

		/// <summary>
		/// Removes all animations from the tracks, leaving skeletons in their last pose. 
		/// It may be desired to use <see cref="AnimationState.SetEmptyAnimations(float)"/> to mix the skeletons back to the setup pose, 
		/// rather than leaving them in their last pose.
		/// </summary>
		public void ClearTrack (int trackIndex) {
			if (trackIndex >= tracks.Count) return;
			TrackEntry current = tracks.Items[trackIndex];
			if (current == null) return;

			queue.End(current);

			DisposeNext(current);

			TrackEntry entry = current;
			while (true) {
				TrackEntry from = entry.mixingFrom;
				if (from == null) break;
				queue.End(from);
				entry.mixingFrom = null;
				entry = from;
			}

			tracks.Items[current.trackIndex] = null;

			queue.Drain();
		}

		private void SetCurrent (int index, TrackEntry entry) {
			TrackEntry current = ExpandToIndex(index);
			tracks.Items[index] = entry;

			if (current != null) {
				queue.Interrupt(current);
				entry.mixingFrom = current;
				entry.mixTime = Math.Max(0, entry.mixDuration - current.trackTime);
				current.timelinesRotation.Clear(); // BOZO - Needed? Recursive?
			}

			queue.Start(entry);
		}

		/// <seealso cref="SetAnimation(int, Animation, bool)" />
		public TrackEntry SetAnimation (int trackIndex, String animationName, bool loop) {
			Animation animation = data.skeletonData.FindAnimation(animationName);
			if (animation == null) throw new ArgumentException("Animation not found: " + animationName, "animationName");
			return SetAnimation(trackIndex, animation, loop);
		}

		/// <summary>Sets the current animation for a track, discarding any queued animations.</summary>
		/// <returns>
		/// A track entry to allow further customization of animation playback. References to the track entry must not be kept 
		/// after <see cref="AnimationState.Dispose"/>.
		/// </returns>
		public TrackEntry SetAnimation (int trackIndex, Animation animation, bool loop) {
			if (animation == null) throw new ArgumentNullException("animation", "animation cannot be null.");
			TrackEntry current = ExpandToIndex(trackIndex);
			if (current != null) {
				if (current.nextTrackLast == -1) {
					// Don't mix from an entry that was never applied.
					tracks.Items[trackIndex] = null;
					queue.Interrupt(current);
					queue.End(current);
					DisposeNext(current);
					current = null;
				} else {
					DisposeNext(current);
				}
			}
			TrackEntry entry = NewTrackEntry(trackIndex, animation, loop, current);
			SetCurrent(trackIndex, entry);
			queue.Drain();
			return entry;
		}

		/// <seealso cref="AddAnimation(int, Animation, bool, float)" />
		public TrackEntry AddAnimation (int trackIndex, String animationName, bool loop, float delay) {
			Animation animation = data.skeletonData.FindAnimation(animationName);
			if (animation == null) throw new ArgumentException("Animation not found: " + animationName, "animationName");
			return AddAnimation(trackIndex, animation, loop, delay);
		}

		/// <summary>Adds an animation to be played delay seconds after the current or last queued animation.</summary>
		/// <param name="delay">
		/// Seconds to begin this animation after the start of the previous animation. May be &lt;= 0 to use the animation
		/// duration of the previous track minus any mix duration plus the negative delay.
		/// </param>
		/// <returns>A track entry to allow further customization of animation playback. References to the track entry must not be kept 
		/// after <see cref="AnimationState.Dispose"/></returns>
		public TrackEntry AddAnimation (int trackIndex, Animation animation, bool loop, float delay) {
			if (animation == null) throw new ArgumentNullException("animation", "animation cannot be null.");

			TrackEntry last = ExpandToIndex(trackIndex);
			if (last != null) {
				while (last.next != null)
					last = last.next;
			}

			TrackEntry entry = NewTrackEntry(trackIndex, animation, loop, last);

			if (last == null) {
				SetCurrent(trackIndex, entry);
				queue.Drain();
			} else {
				last.next = entry;
				if (delay <= 0) {
					float duration = last.animationEnd - last.animationStart;
					if (duration != 0)
						delay += duration * (1 + (int)(last.trackTime / duration)) - data.GetMix(last.animation, animation);
					else
						delay = 0;
				}
			}

			entry.delay = delay;
			return entry;
		}

		/// <summary>
		/// Sets an empty animation for a track, discarding any queued animations, and mixes to it over the specified mix duration.</summary>
		public TrackEntry SetEmptyAnimation (int trackIndex, float mixDuration) {
			TrackEntry entry = SetAnimation(trackIndex, AnimationState.EmptyAnimation, false);
			entry.mixDuration = mixDuration;
			entry.trackEnd = mixDuration;
			return entry;
		}

		/// <summary>
		/// Adds an empty animation to be played after the current or last queued animation for a track, and mixes to it over the 
		/// specified mix duration.</summary>
		/// <returns>
		/// A track entry to allow further customization of animation playback. References to the track entry must not be kept after <see cref="AnimationState.Dispose"/>.
		/// </returns>
		/// <param name="trackIndex">Track number.</param>
		/// <param name="mixDuration">Mix duration.</param>
		/// <param name="delay">Seconds to begin this animation after the start of the previous animation. May be &lt;= 0 to use the animation 
		/// duration of the previous track minus any mix duration plus the negative delay.</param>
		public TrackEntry AddEmptyAnimation (int trackIndex, float mixDuration, float delay) {
			if (delay <= 0) delay -= mixDuration;
			TrackEntry entry = AddAnimation(trackIndex, AnimationState.EmptyAnimation, false, delay);
			entry.mixDuration = mixDuration;
			entry.trackEnd = mixDuration;
			return entry;
		}
			
		/// <summary>
		/// Sets an empty animation for every track, discarding any queued animations, and mixes to it over the specified mix duration.</summary>
		public void SetEmptyAnimations (float mixDuration) {
			queue.drainDisabled = true;
			for (int i = 0, n = tracks.Count; i < n; i++) {
				TrackEntry current = tracks.Items[i];
				if (current != null) SetEmptyAnimation(i, mixDuration);
			}
			queue.drainDisabled = false;
			queue.Drain();
		}

		private TrackEntry ExpandToIndex (int index) {
			if (index < tracks.Count) return tracks.Items[index];
			while (index >= tracks.Count)
				tracks.Add(null);			
			return null;
		}

		/// <param name="last">May be null.</param>
		private TrackEntry NewTrackEntry (int trackIndex, Animation animation, bool loop, TrackEntry last) {
			return new TrackEntry {
				trackIndex = trackIndex,
				animation = animation,
				loop = loop,

				eventThreshold = 0,
				attachmentThreshold = 0,
				drawOrderThreshold = 0,

				animationStart = 0,
				animationEnd = animation.duration,
				animationLast = -1,
				nextAnimationLast = -1,

				delay = 0,
				trackTime = 0,
				trackLast = -1,
				nextTrackLast = -1,
				trackEnd = loop ? int.MaxValue : animation.duration,
				timeScale = 1,

				alpha = 1,
				mixTime = 0,
				mixDuration = (last == null) ? 0 : data.GetMix(last.animation, animation),
			};
		}

		private void DisposeNext (TrackEntry entry) {
			TrackEntry next = entry.next;
			while (next != null) {
				queue.Dispose(next);
				next = next.next;
			}
			entry.next = null;
		}

		private void AnimationsChanged () {
			animationsChanged = false;

			// Compute timelinesFirst from lowest to highest track entries.
			int i = 0, n = tracks.Count;
			var tracksItems = tracks.Items;
			propertyIDs.Clear();
			for (; i < n; i++) { // Find first non-null entry.
				TrackEntry entry = tracksItems[i];
				if (entry == null) continue;
				SetTimelinesFirst(entry);
				i++;
				break;
			}
			for (; i < n; i++) { // Rest of entries.
				TrackEntry entry = tracksItems[i];
				if (entry != null) CheckTimelinesFirst(entry);
			}

			// Compute timelinesLast from highest to lowest track entries that have mixingFrom.
			propertyIDs.Clear();
			int lowestMixingFrom = n;
			for (i = 0; i < n; i++) { // Find lowest with a mixingFrom entry.
				TrackEntry entry = tracksItems[i];
				if (entry == null) continue;
				if (entry.mixingFrom != null) {
					lowestMixingFrom = i;
					break;
				}
			}
			for (i = n - 1; i >= lowestMixingFrom; i--) {
				TrackEntry entry = tracksItems[i];
				if (entry == null) continue;

				var timelines = entry.animation.timelines;
				var timelinesItems = timelines.Items;
				for (int ii = 0, nn = timelines.Count; ii < nn; ii++)
					propertyIDs.Add(timelinesItems[ii].PropertyId);

				entry = entry.mixingFrom;
				while (entry != null) {
					CheckTimelinesUsage(entry, entry.timelinesLast);
					entry = entry.mixingFrom;
				}
			}
		}

		/// <summary>From last to first mixingFrom entries, sets timelinesFirst to true on last, calls checkTimelineUsage on rest.</summary>
		private void SetTimelinesFirst (TrackEntry entry) {
			if (entry.mixingFrom != null) {
				SetTimelinesFirst(entry.mixingFrom);
				CheckTimelinesUsage(entry, entry.timelinesFirst);
				return;
			}
			var propertyIDs = this.propertyIDs;
			var timelines = entry.animation.timelines;
			int n = timelines.Count;
			entry.timelinesFirst.EnsureCapacity(n); // entry.timelinesFirst.setSize(n);
			var usage = entry.timelinesFirst.Items;
			for (int i = 0; i < n; i++) {
				propertyIDs.Add(timelines.Items[i].PropertyId);
				usage[i] = true;
			}
		}

		/// <summary>From last to first mixingFrom entries, calls checkTimelineUsage.</summary>
		private void CheckTimelinesFirst (TrackEntry entry) {
			if (entry.mixingFrom != null) CheckTimelinesFirst(entry.mixingFrom);
			CheckTimelinesUsage(entry, entry.timelinesFirst);
		}

		private void CheckTimelinesUsage (TrackEntry entry, ExposedList<bool> usageArray) {
			var propertyIDs = this.propertyIDs;
			var timelines = entry.animation.timelines;
			int n = timelines.Count;
			usageArray.EnsureCapacity(n);
			var usage = usageArray.Items;
			var timelinesItems = timelines.Items;
			for (int i = 0; i < n; i++)
				usage[i] = propertyIDs.Add(timelinesItems[i].PropertyId);
		}

		/// <returns>The track entry for the animation currently playing on the track, or null.</returns>
		public TrackEntry GetCurrent (int trackIndex) {
			return (trackIndex >= tracks.Count) ? null : tracks.Items[trackIndex];
		}

		override public String ToString () {
			var buffer = new StringBuilder();
			for (int i = 0, n = tracks.Count; i < n; i++) {
				TrackEntry entry = tracks.Items[i];
				if (entry == null) continue;
				if (buffer.Length > 0) buffer.Append(", ");
				buffer.Append(entry.ToString());
			}
			return buffer.Length == 0 ? "<none>" : buffer.ToString();
		}

		internal void OnStart (TrackEntry entry) { if (Start != null) Start(entry); }
		internal void OnInterrupt (TrackEntry entry) { if (Interrupt != null) Interrupt(entry); }
		internal void OnEnd (TrackEntry entry) { if (End != null) End(entry); }
		internal void OnDispose (TrackEntry entry) { if (Dispose != null) Dispose(entry); }
		internal void OnComplete (TrackEntry entry) { if (Complete != null) Complete(entry); }
		internal void OnEvent (TrackEntry entry, Event e) { if (Event != null) Event(entry, e); }
	}

	public class TrackEntry {
		internal Animation animation;

		internal TrackEntry next, mixingFrom;
		internal int trackIndex;

		internal bool loop;
		internal float eventThreshold, attachmentThreshold, drawOrderThreshold;
		internal float animationStart, animationEnd, animationLast, nextAnimationLast;
		internal float delay, trackTime, trackLast, nextTrackLast, trackEnd, timeScale = 1f;
		internal float alpha, mixTime, mixDuration;
		internal readonly ExposedList<bool> timelinesFirst = new ExposedList<bool>(), timelinesLast = new ExposedList<bool>();
		internal readonly ExposedList<float> timelinesRotation = new ExposedList<float>();

		public int TrackIndex { get { return trackIndex; } }
		public Animation Animation { get { return animation; } }
		public bool Loop { get { return loop; } set { loop = value; } }

		///<summary>
		/// Seconds to postpone playing the animation. When a track entry is the current track entry, delay postpones incrementing 
		/// the track time. When a track entry is queued, delay is the time from the start of the previous animation to when the 
		/// track entry will become the current track entry.</summary>
		public float Delay { get { return delay; } set { delay = value; } }

		/// <summary>
		/// Current time in seconds this track entry has been the current track entry. The track time determines 
		/// <see cref="TrackEntry.AnimationTime"/>. The track time can be set to start the animation at a time other than 0, without affecting looping.</summary>
		public float TrackTime { get { return trackTime; } set { trackTime = value; } }

		/// <summary>
		/// The track time in seconds when this animation will be removed from the track. Defaults to the animation duration for 
		/// non-looping animations and to <see cref="int.MaxValue"/> for looping animations. If the track end time is reached and no 
		/// other animations are queued for playback, and mixing from any previous animations is complete, then the track is cleared, 
		/// leaving skeletons in their last pose.
		/// 
		/// It may be desired to use <see cref="AnimationState.AddEmptyAnimation(int, float, float)"/> to mix the skeletons back to the 
		/// setup pose, rather than leaving them in their last pose.
		/// </summary>
		public float TrackEnd { get { return trackEnd; } set { trackEnd = value; } }

		/// <summary>
		/// Seconds when this animation starts, both initially and after looping. Defaults to 0.
		/// 
		/// When changing the animation start time, it often makes sense to set <see cref="TrackEntry.AnimationLast"/> to the same value to 
		/// prevent timeline keys before the start time from triggering.
		/// </summary>
		public float AnimationStart { get { return animationStart; } set { animationStart = value; } }

		/// <summary>
		/// Seconds for the last frame of this animation. Non-looping animations won't play past this time. Looping animations will 
		/// loop back to <see cref="TrackEntry.AnimationStart"/> at this time. Defaults to the animation duration.</summary>
		public float AnimationEnd { get { return animationEnd; } }

		/// <summary>
		/// The time in seconds this animation was last applied. Some timelines use this for one-time triggers. Eg, when this
		/// animation is applied, event timelines will fire all events between the animation last time (exclusive) and animation time 
		/// (inclusive). Defaults to -1 to ensure triggers on frame 0 happen the first time this animation is applied.</summary>
		public float AnimationLast {
			get { return animationLast; }
			set {
				animationLast = value;
				nextAnimationLast = value;
			}
		}

		/// <summary>
		/// Uses <see cref="TrackEntry.TrackTime"/> to compute the animation time between <see cref="TrackEntry.AnimationStart"/>. and
		/// <see cref="TrackEntry.AnimationEnd"/>. When the track time is 0, the animation time is equal to the animation start time.
		/// </summary>
		public float AnimationTime {
			get {
				if (loop) {
					float duration = animationEnd - animationStart;
					if (duration == 0) return animationStart;
					return (trackTime % duration) + animationStart;
				}
				return Math.Min(trackTime + animationStart, animationEnd);
			}
		}

		/// <summary>
		/// Multiplier for the delta time when the animation state is updated, causing time for this animation to play slower or 
		/// faster. Defaults to 1.
		/// </summary>
		public float TimeScale { get { return timeScale; } set { timeScale = value; } }

		/// <summary>
		/// Values less than 1 mix this animation with the last skeleton pose. Defaults to 1, which overwrites the last skeleton pose with 
		/// this animation.
		/// 
		/// Typically track 0 is used to completely pose the skeleton, then alpha can be used on higher tracks. It doesn't make sense 
		/// to use alpha on track 0 if the skeleton pose is from the last frame render. 
		/// </summary>
		public float Alpha { get { return alpha; } set { alpha = value; } }

		/// <summary>
		/// When the mix percentage (mix time / mix duration) is less than the event threshold, event timelines for the animation 
		/// being mixed out will be applied. Defaults to 0, so event timelines are not applied for an animation being mixed out.</summary>
		public float EventThreshold { get { return eventThreshold; } set { eventThreshold = value; } }

		/// <summary>
		/// When the mix percentage (mix time / mix duration) is less than the attachment threshold, attachment timelines for the 
		/// animation being mixed out will be applied. Defaults to 0, so attachment timelines are not applied for an animation being 
		/// mixed out.</summary>
		public float AttachmentThreshold { get { return attachmentThreshold; } set { attachmentThreshold = value; } }

		/// <summary>
		/// When the mix percentage (mix time / mix duration) is less than the draw order threshold, draw order timelines for the 
		/// animation being mixed out will be applied. Defaults to 0, so draw order timelines are not applied for an animation being 
		/// mixed out.
		/// </summary>
		public float DrawOrderThreshold { get { return drawOrderThreshold; } set { drawOrderThreshold = value; } }

		/// <summary>
		/// The animation queued to start after this animation, or null.</summary>
		public TrackEntry Next { get { return next; } }

		/// <summary>
		/// Returns true if at least one loop has been completed.</summary>
		public bool IsComplete {
			get { return trackTime >= animationEnd - animationStart; }
		}

		/// <summary>
		/// Seconds from 0 to the mix duration when mixing from the previous animation to this animation. May be slightly more than 
		/// <see cref="TrackEntry.MixDuration"/>.</summary>
		public float MixTime { get { return mixTime; } set { mixTime = value; } }

		/// <summary>
		/// Seconds for mixing from the previous animation to this animation. Defaults to the value provided by 
		/// <see cref="AnimationStateData"/> based on the animation before this animation (if any).
		/// 
		/// The mix duration must be set before <see cref="AnimationState.Update(float)"/> is next called.
		/// </summary>
		public float MixDuration { get { return mixDuration; } set { mixDuration = value; } }

		/// <summary>
		/// The track entry for the previous animation when mixing from the previous animation to this animation, or null if no 
		/// mixing is currently occuring.</summary>
		public TrackEntry MixingFrom { get { return mixingFrom; } }

		public event AnimationState.TrackEntryDelegate Start, Interrupt, End, Dispose, Complete;
		public event AnimationState.TrackEntryEventDelegate Event;
		internal void OnStart () { if (Start != null) Start(this); }
		internal void OnInterrupt () { if (Interrupt != null) Interrupt(this); }
		internal void OnEnd () { if (End != null) End(this); }
		internal void OnDispose () { if (Dispose != null) Dispose(this); }
		internal void OnComplete () { if (Complete != null) Complete(this); }
		internal void OnEvent (Event e) { if (Event != null) Event(this, e); }

		override public String ToString () {
			return animation == null ? "<none>" : animation.name;
		}
	}

	enum EventType {
		Start, Interrupt, End, Dispose, Complete, Event
	}

	class EventQueue {
		private readonly ExposedList<EventQueueEntry> eventQueueEntries = new ExposedList<EventQueueEntry>();
		public bool drainDisabled;

		private readonly AnimationState state;
		public event Action AnimationsChanged;

		public EventQueue (AnimationState state, Action HandleAnimationsChanged) {
			this.state = state;
			this.AnimationsChanged += HandleAnimationsChanged;
		}

		struct EventQueueEntry {
			public EventType type;
			public TrackEntry entry;
			public Event e;

			public EventQueueEntry (EventType eventType, TrackEntry trackEntry, Event e = null) {
				this.type = eventType;
				this.entry = trackEntry;
				this.e = e;
			}
		}

		public void Start (TrackEntry entry) {
			eventQueueEntries.Add(new EventQueueEntry(EventType.Start, entry));
			if (AnimationsChanged != null) AnimationsChanged();
		}

		public void Interrupt (TrackEntry entry) { eventQueueEntries.Add(new EventQueueEntry(EventType.Interrupt, entry)); }

		public void End (TrackEntry entry) {
			eventQueueEntries.Add(new EventQueueEntry(EventType.End, entry));
			if (AnimationsChanged != null) AnimationsChanged();
		}

		public void Dispose (TrackEntry entry) {
			eventQueueEntries.Add(new EventQueueEntry(EventType.Dispose, entry));
		}

		public void Complete (TrackEntry entry) {
			eventQueueEntries.Add(new EventQueueEntry(EventType.Complete, entry));
		}

		public void Event (TrackEntry entry, Event e) {
			eventQueueEntries.Add(new EventQueueEntry(EventType.Event, entry, e));
		}

		public void Drain () {
			if (drainDisabled) return;
			drainDisabled = true;

			var entries = this.eventQueueEntries;
			var entriesItems = entries.Items;
			AnimationState state = this.state;


			for (int i = 0, n = entries.Count; i < n; i++) {
				var queueEntry = entriesItems[i];
				TrackEntry trackEntry = queueEntry.entry;

				switch (queueEntry.type) {
				case EventType.Start:
					trackEntry.OnStart();
					state.OnStart(trackEntry);
					break;
				case EventType.Interrupt:
					trackEntry.OnInterrupt();
					state.OnInterrupt(trackEntry);
					break;
				case EventType.End:
					trackEntry.OnEnd();
					state.OnEnd(trackEntry);
					break;
				case EventType.Dispose:
					trackEntry.OnDispose();
					state.OnDispose(trackEntry);
					break;
				case EventType.Complete:
					trackEntry.OnComplete();
					state.OnComplete(trackEntry);
					break;
				case EventType.Event:
					trackEntry.OnEvent(queueEntry.e);
					state.OnEvent(trackEntry, queueEntry.e);
					break;
				}
			}
			eventQueueEntries.Clear();

			drainDisabled = false;
		}
	}
}
