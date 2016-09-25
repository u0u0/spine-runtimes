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

namespace Spine {
	public class Bone : IUpdatable {
		static public bool yDown;

		internal BoneData data;
		internal Skeleton skeleton;
		internal Bone parent;
		internal ExposedList<Bone> children = new ExposedList<Bone>();
		internal float x, y, rotation, scaleX, scaleY, shearX, shearY;
		internal float ax, ay, arotation, ascaleX, ascaleY, ashearX, ashearY;
		internal bool appliedValid;

		internal float a, b, worldX;
		internal float c, d, worldY;

//		internal float worldSignX, worldSignY;
//		public float WorldSignX { get { return worldSignX; } }
//		public float WorldSignY { get { return worldSignY; } }

		internal bool sorted;

		public BoneData Data { get { return data; } }
		public Skeleton Skeleton { get { return skeleton; } }
		public Bone Parent { get { return parent; } }
		public ExposedList<Bone> Children { get { return children; } }
		public float X { get { return x; } set { x = value; } }
		public float Y { get { return y; } set { y = value; } }
		public float Rotation { get { return rotation; } set { rotation = value; } }
		/// <summary>The rotation, as calculated by any constraints.</summary>
		public float AppliedRotation { get { return arotation; } set { arotation = value; } }
		public float ScaleX { get { return scaleX; } set { scaleX = value; } }
		public float ScaleY { get { return scaleY; } set { scaleY = value; } }
		public float ShearX { get { return shearX; } set { shearX = value; } }
		public float ShearY { get { return shearY; } set { shearY = value; } }

		public float A { get { return a; } }
		public float B { get { return b; } }
		public float C { get { return c; } }
		public float D { get { return d; } }
		public float WorldX { get { return worldX; } }
		public float WorldY { get { return worldY; } }
		public float WorldRotationX { get { return MathUtils.Atan2(c, a) * MathUtils.RadDeg; } }
		public float WorldRotationY { get { return MathUtils.Atan2(d, b) * MathUtils.RadDeg; } }

		/// <summary>Returns the magnitide (always positive) of the world scale X.</summary>
		public float WorldScaleX { get { return (float)Math.Sqrt(a * a + c * c); } }
		/// <summary>Returns the magnitide (always positive) of the world scale Y.</summary>
		public float WorldScaleY { get { return (float)Math.Sqrt(b * b + d * d); } }

		/// <param name="parent">May be null.</param>
		public Bone (BoneData data, Skeleton skeleton, Bone parent) {
			if (data == null) throw new ArgumentNullException("data", "data cannot be null.");
			if (skeleton == null) throw new ArgumentNullException("skeleton", "skeleton cannot be null.");
			this.data = data;
			this.skeleton = skeleton;
			this.parent = parent;
			SetToSetupPose();
		}

		/// <summary>Same as <see cref="UpdateWorldTransform"/>. This method exists for Bone to implement <see cref="Spine.IUpdatable"/>.</summary>
		public void Update () {
			UpdateWorldTransform(x, y, rotation, scaleX, scaleY, shearX, shearY);
		}

		/// <summary>Computes the world transform using the parent bone and this bone's local transform.</summary>
		public void UpdateWorldTransform () {
			UpdateWorldTransform(x, y, rotation, scaleX, scaleY, shearX, shearY);
		}

		/// <summary>Computes the world transform using the parent bone and the specified local transform.</summary>
		public void UpdateWorldTransform (float x, float y, float rotation, float scaleX, float scaleY, float shearX, float shearY) {
			ax = x;
			ay = y;
			arotation = rotation;
			ascaleX = scaleX;
			ascaleY = scaleY;
			ashearX = shearX;
			ashearY = shearY;
			appliedValid = true;
			Skeleton skeleton = this.skeleton;

			Bone parent = this.parent;
			if (parent == null) { // Root bone.
				float rotationY = rotation + 90 + shearY;
				float la = MathUtils.CosDeg(rotation + shearX) * scaleX;
				float lb = MathUtils.CosDeg(rotationY) * scaleY;
				float lc = MathUtils.SinDeg(rotation + shearX) * scaleX;
				float ld = MathUtils.SinDeg(rotationY) * scaleY;
				if (skeleton.flipX) {
					x = -x;
					la = -la;
					lb = -lb;
				}
				if (skeleton.flipY != yDown) {
					y = -y;
					lc = -lc;
					ld = -ld;
				}
				a = la;
				b = lb;
				c = lc;
				d = ld;
				worldX = x + skeleton.x;
				worldY = y + skeleton.y;
//				worldSignX = Math.Sign(scaleX);
//				worldSignY = Math.Sign(scaleY);
				return;
			}

			float pa = parent.a, pb = parent.b, pc = parent.c, pd = parent.d;
			worldX = pa * x + pb * y + parent.worldX;
			worldY = pc * x + pd * y + parent.worldY;
//			worldSignX = parent.worldSignX * Math.Sign(scaleX);
//			worldSignY = parent.worldSignY * Math.Sign(scaleY);

			switch (data.transformMode) {
			case TransformMode.Normal: {
					float rotationY = rotation + 90 + shearY;
					float la = MathUtils.CosDeg(rotation + shearX) * scaleX;
					float lb = MathUtils.CosDeg(rotationY) * scaleY;
					float lc = MathUtils.SinDeg(rotation + shearX) * scaleX;
					float ld = MathUtils.SinDeg(rotationY) * scaleY;
					a = pa * la + pb * lc;
					b = pa * lb + pb * ld;
					c = pc * la + pd * lc;
					d = pc * lb + pd * ld;	
					return;
				}
			case TransformMode.OnlyTranslation: {
					float rotationY = rotation + 90 + shearY;
					a = MathUtils.CosDeg(rotation + shearX) * scaleX;
					b = MathUtils.CosDeg(rotationY) * scaleY;
					c = MathUtils.SinDeg(rotation + shearX) * scaleX;
					d = MathUtils.SinDeg(rotationY) * scaleY;
					break;
				}
			case TransformMode.NoRotation: {
					if (false) {
						// Summing parent rotations.
						// 1) Negative parent scale causes bone to rotate.
						float sum = 0;
						Bone current = parent;
						while (current != null) {
							sum += current.arotation;
							current = current.parent;
						}
						rotation -= sum;
						float rotationY = rotation + 90 + shearY;
						float la = MathUtils.CosDeg(rotation + shearX) * scaleX;
						float lb = MathUtils.CosDeg(rotationY) * scaleY;
						float lc = MathUtils.SinDeg(rotation + shearX) * scaleX;
						float ld = MathUtils.SinDeg(rotationY) * scaleY;
						a = pa * la + pb * lc;
						b = pa * lb + pb * ld;
						c = pc * la + pd * lc;
						d = pc * lb + pd * ld;	
					} else if (true) {
						// Old way.
						// 1) Immediate parent scale is applied in wrong direction.
						// 2) Negative parent scale causes bone to rotate.
						pa = 1;
						pb = 0;
						pc = 0;
						pd = 1;
						float rotationY, la, lb, lc, ld;
						do {
							if (!parent.appliedValid) parent.UpdateAppliedTransform();
							float pr = parent.arotation, psx = parent.ascaleX;
							rotationY = pr + 90 + parent.ashearY;
							la = MathUtils.CosDeg(pr + parent.shearX);
							lb = MathUtils.CosDeg(rotationY);
							lc = MathUtils.SinDeg(pr + parent.shearX);
							ld = MathUtils.SinDeg(rotationY);
							float temp = (pa * la + pb * lc) * psx;
							pb = (pb * ld + pa * lb) * parent.ascaleY;
							pa = temp;
							temp = (pc * la + pd * lc) * psx;
							pd = (pd * ld + pc * lb) * parent.ascaleY;
							pc = temp;

							if (psx < 0) lc = -lc;
							temp = pa * la - pb * lc;
							pb = pb * ld - pa * lb;
							pa = temp;
							temp = pc * la - pd * lc;
							pd = pd * ld - pc * lb;
							pc = temp;

							switch (parent.data.transformMode) {
							case TransformMode.NoScale:
							case TransformMode.NoScaleOrReflection:
								goto outer;
							}
							parent = parent.parent;
						} while (parent != null);
						outer:
						rotationY = rotation + 90 + shearY;
						la = MathUtils.CosDeg(rotation + shearX) * scaleX;
						lb = MathUtils.CosDeg(rotationY) * scaleY;
						lc = MathUtils.SinDeg(rotation + shearX) * scaleX;
						ld = MathUtils.SinDeg(rotationY) * scaleY;
						a = pa * la + pb * lc;
						b = pa * lb + pb * ld;
						c = pc * la + pd * lc;
						d = pc * lb + pd * ld;
					} else {
						// New way.
						// 1) Negative scale can cause bone to flip.
						float psx = (float)Math.Sqrt(pa * pa + pc * pc), psy, pr;
						if (psx > 0.0001f) {
							float det = pa * pd - pb * pc;
							psy = det / psx;
							pr = MathUtils.Atan2(pc, pa) * MathUtils.RadDeg;
						} else {
							psx = 0;
							psy = (float)Math.Sqrt(pb * pb + pd * pd);
							pr = 90 - MathUtils.Atan2(pd, pb) * MathUtils.RadDeg;
						}
						float blend;
						if (pr < -90)
							blend = 1 + (pr + 90) / 90;
						else if (pr < 0)
							blend = -pr / 90;
						else if (pr < 90)
							blend = pr / 90;
						else
							blend = 1 - (pr - 90) / 90;
						pa = psx + (Math.Abs(psy) * Math.Sign(psx) - psx) * blend;
						pd = psy + (Math.Abs(psx) * Math.Sign(psy) - psy) * blend;
						float rotationY = rotation + 90 + shearY;
						a = pa * MathUtils.CosDeg(rotation + shearX) * scaleX;
						b = pa * MathUtils.CosDeg(rotationY) * scaleY;
						c = pd * MathUtils.SinDeg(rotation + shearX) * scaleX;
						d = pd * MathUtils.SinDeg(rotationY) * scaleY;
					}
					break;
				}
			case TransformMode.NoScale:
			case TransformMode.NoScaleOrReflection: {
					float cos = MathUtils.CosDeg(rotation), sin = MathUtils.SinDeg(rotation);
					float za = pa * cos + pb * sin, zb = za;
					float zc = pc * cos + pd * sin, zd = zc;
					float s = (float)Math.Sqrt(za * za + zc * zc);
					if (s > 0.00001f) s = 1 / s;
					za *= s;
					zc *= s;
					s = (float)Math.Sqrt(zb * zb + zd * zd);
					if (s > 0.00001f) s = 1 / s;
					zb *= s;
					zd *= s;
					float by = MathUtils.Atan2(zd, zb), r = MathUtils.PI / 2 - (by - MathUtils.Atan2(zc, za));
					if (r > MathUtils.PI)
						r -= MathUtils.PI2;
					else if (r < -MathUtils.PI) r += MathUtils.PI2;
					r += by;
					s = (float)Math.Sqrt(zb * zb + zd * zd);
					zb = MathUtils.Cos(r) * s;
					zd = MathUtils.Sin(r) * s;
					float la = MathUtils.CosDeg(shearX) * scaleX;
					float lb = MathUtils.CosDeg(90 + shearY) * scaleY;
					float lc = MathUtils.SinDeg(shearX) * scaleX;
					float ld = MathUtils.SinDeg(90 + shearY) * scaleY;
					a = za * la + zb * lc;
					b = za * lb + zb * ld;
					c = zc * la + zd * lc;
					d = zc * lb + zd * ld;
					if (data.transformMode != TransformMode.NoScaleOrReflection ? pa * pd - pb * pc < 0 : skeleton.flipX != skeleton.flipY) {
						b = -b;
						d = -d;
					}
					return;
				}
			}

			if (skeleton.flipX) {
				a = -a;
				b = -b;
			}
			if (skeleton.flipY) {
				c = -c;
				d = -d;
			}
		}

		public void SetToSetupPose () {
			BoneData data = this.data;
			x = data.x;
			y = data.y;
			rotation = data.rotation;
			scaleX = data.scaleX;
			scaleY = data.scaleY;
			shearX = data.shearX;
			shearY = data.shearY;
		}

		public float WorldToLocalRotationX {
			get {
				Bone parent = this.parent;
				if (parent == null) return arotation;
				float pa = parent.a, pb = parent.b, pc = parent.c, pd = parent.d, a = this.a, c = this.c;
				return MathUtils.Atan2(pa * c - pc * a, pd * a - pb * c) * MathUtils.RadDeg;
			}
		}

		public float WorldToLocalRotationY {
			get {
				Bone parent = this.parent;
				if (parent == null) return arotation;
				float pa = parent.a, pb = parent.b, pc = parent.c, pd = parent.d, b = this.b, d = this.d;
				return MathUtils.Atan2(pa * d - pc * b, pd * b - pb * d) * MathUtils.RadDeg;
			}
		}

		public void RotateWorld (float degrees) {
			float a = this.a, b = this.b, c = this.c, d = this.d;
			float cos = MathUtils.CosDeg(degrees), sin = MathUtils.SinDeg(degrees);
			this.a = cos * a - sin * c;
			this.b = cos * b - sin * d;
			this.c = sin * a + cos * c;
			this.d = sin * b + cos * d;
			appliedValid = false;
		}

		/// <summary>
		/// Computes the individual applied transform values from the world transform. This can be useful to perform processing using
		/// the applied transform after the world transform has been modified directly (eg, by a constraint)..
		/// 
		/// Some information is ambiguous in the world transform, such as -1,-1 scale versus 180 rotation.
		/// </summary>
		public void UpdateAppliedTransform () {
			appliedValid = true;
			Bone parent = this.parent;
			if (parent == null) {
				ax = worldX;
				ay = worldY;
				arotation = MathUtils.Atan2(c, a) * MathUtils.RadDeg;
				ascaleX = (float)Math.Sqrt(a * a + c * c);
				ascaleY = (float)Math.Sqrt(b * b + d * d);
				ashearX = 0;
				ashearY = MathUtils.Atan2(a * b + c * d, a * d - b * c) * MathUtils.RadDeg;
				return;
			}
			float pa = parent.a, pb = parent.b, pc = parent.c, pd = parent.d;
			float pid = 1 / (pa * pd - pb * pc);
			float dx = worldX - parent.worldX, dy = worldY - parent.worldY;
			ax = (dx * pd * pid - dy * pb * pid);
			ay = (dy * pa * pid - dx * pc * pid);
			float ia = pid * pd;
			float id = pid * pa;
			float ib = pid * pb;
			float ic = pid * pc;
			float ra = ia * a - ib * c;
			float rb = ia * b - ib * d;
			float rc = id * c - ic * a;
			float rd = id * d - ic * b;
			ashearX = 0;
			ascaleX = (float)Math.Sqrt(ra * ra + rc * rc);
			if (ascaleX > 0.0001f) {
				float det = ra * rd - rb * rc;
				ascaleY = det / ascaleX;
				ashearY = MathUtils.Atan2(ra * rb + rc * rd, det) * MathUtils.RadDeg;
				arotation = MathUtils.Atan2(rc, ra) * MathUtils.RadDeg;
			} else {
				ascaleX = 0;
				ascaleY = (float)Math.Sqrt(rb * rb + rd * rd);
				ashearY = 0;
				arotation = 90 - MathUtils.Atan2(rd, rb) * MathUtils.RadDeg;
			}
		}

		public void WorldToLocal (float worldX, float worldY, out float localX, out float localY) {			
			float a = this.a, b = this.b, c = this.c, d = this.d;
			float invDet = 1 / (a * d - b * c);
			float x = worldX - this.worldX, y = worldY - this.worldY;
			localX = (x * d * invDet - y * b * invDet);
			localY = (y * a * invDet - x * c * invDet);
		}

		public void LocalToWorld (float localX, float localY, out float worldX, out float worldY) {
			worldX = localX * a + localY * b + this.worldX;
			worldY = localX * c + localY * d + this.worldY;
		}

		override public String ToString () {
			return data.name;
		}
	}
}
