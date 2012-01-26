﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if MONOMAC
using MonoMac.OpenGL;
#else
using OpenTK.Graphics.ES20;
using ShaderType = OpenTK.Graphics.ES20.All;
using ProgramParameter = OpenTK.Graphics.ES20.All;
#endif

namespace Microsoft.Xna.Framework.Graphics
{
    public class EffectPass
    {
        EffectTechnique _technique = null;
		GraphicsDevice _graphicsDevice;
		
		string name;
		int shaderProgram = 0;
		DXEffectObject.d3dx_state[] states;
		DXShader pixelShader;
		DXShader vertexShader;

		static string passthroughVertexShaderSrc = @"
				uniform mat4 transformMatrix;

				attribute vec4 aPosition;
				attribute vec4 aTexCoord;
				attribute vec4 aColor;

				varying vec4 vTexCoord0;
				varying vec4 vFrontColor;
				void main() {
					vTexCoord0.xy = aTexCoord.xy;
					vFrontColor = aColor;

					gl_Position = transformMatrix * aPosition;
				}";
		static int? passthroughVertexShader;

		bool passthroughVertexShaderAttached = false;
		
		public EffectPass(EffectTechnique technique, DXEffectObject.d3dx_pass pass)
        {
            _technique = technique;
			_graphicsDevice = _technique._effect.GraphicsDevice;
			
			name = pass.name;
			states = pass.states;
			
			Console.WriteLine (technique.Name);
			
			shaderProgram = GL.CreateProgram ();
			
			// Set the parameters
			//is this nesesary, or just for VR?
			/*GL.ProgramParameter (shaderProgram,
				AssemblyProgramParameterArb.GeometryInputType,(int)All.Lines);
			GL.ProgramParameter (shaderProgram,
				AssemblyProgramParameterArb.GeometryOutputType, (int)All.Line);*/
			
			// Set the max vertices
			/*int maxVertices;
			GL.GetInteger (GetPName.MaxGeometryOutputVertices, out maxVertices);
			GL.ProgramParameter (shaderProgram,
				AssemblyProgramParameterArb.GeometryVerticesOut, maxVertices);*/
			
			bool needPixelShader = false;
			bool needVertexShader = false;
			foreach ( DXEffectObject.d3dx_state state in states) {
				if (state.operation.class_ == DXEffectObject.STATE_CLASS.PIXELSHADER) {
					needPixelShader = true;
					if (state.type == DXEffectObject.STATE_TYPE.CONSTANT) {
						pixelShader = (DXShader)state.parameter.data;
						GL.AttachShader (shaderProgram, pixelShader.shader);
					}
				} else if (state.operation.class_ == DXEffectObject.STATE_CLASS.VERTEXSHADER) {
					needVertexShader = true;
					if (state.type == DXEffectObject.STATE_TYPE.CONSTANT) {
						vertexShader = (DXShader)state.parameter.data;
						GL.AttachShader (shaderProgram, vertexShader.shader);
					}
				} else {
					throw new NotImplementedException();
				}
			}
			
			//If we have what we need, link now
			if ( (needPixelShader == (pixelShader != null)) &&
				 (needVertexShader == (vertexShader != null))) {
				Link();
			}
			
        }

		private void Link ()
		{
			if (vertexShader == null && !passthroughVertexShaderAttached) {
				if (!passthroughVertexShader.HasValue) {
					int shader = GL.CreateShader(ShaderType.VertexShader);
#if IPHONE
					GL.ShaderSource (shader, 1,
					                new string[]{passthroughVertexShaderSrc}, (int[])null);
#else
					GL.ShaderSource(shader, passthroughVertexShaderSrc);
#endif

					GL.CompileShader(shader);

					passthroughVertexShader = shader;
				}

				GL.AttachShader(shaderProgram, passthroughVertexShader.Value);
#if !ES11
				GL.BindAttribLocation(shaderProgram, GraphicsDevice.attributePosition, "aPosition");
				GL.BindAttribLocation(shaderProgram, GraphicsDevice.attributeTexCoord, "aTexCoord");
				GL.BindAttribLocation(shaderProgram, GraphicsDevice.attributeColor, "aColor");
#endif

				passthroughVertexShaderAttached = true;
			} else if (vertexShader != null && passthroughVertexShaderAttached) {
				GL.DetachShader(shaderProgram, passthroughVertexShader.Value);
				passthroughVertexShaderAttached = false;
			}

			if (vertexShader != null) {
				vertexShader.OnLink(shaderProgram);
			}
			if (pixelShader != null) {
				pixelShader.OnLink (shaderProgram);
			}

			GL.LinkProgram (shaderProgram);

			int linked = 0;
#if IPHONE
			GL.GetProgram (shaderProgram, ProgramParameter.LinkStatus, ref linked);
#else
			GL.GetProgram (shaderProgram, ProgramParameter.LinkStatus, out linked);
#endif
			if (linked == 0) {
#if !IPHONE
				string log = GL.GetProgramInfoLog(shaderProgram);
				Console.WriteLine (log);
#endif
				throw new InvalidOperationException("Unable to link effect program");
			}

		}
		
		public void Apply ()
		{
			_technique._effect.OnApply();
			//Console.WriteLine (_technique._effect.Name+" - "+_technique.Name+" - "+Name);
			bool relink = false;
			foreach ( DXEffectObject.d3dx_state state in states) {
				
				//constants handled on init
				if (state.type == DXEffectObject.STATE_TYPE.CONSTANT) continue;
				
				if (state.operation.class_ == DXEffectObject.STATE_CLASS.PIXELSHADER ||
					state.operation.class_ == DXEffectObject.STATE_CLASS.VERTEXSHADER) {
					
					DXShader shader;
					switch (state.type) {
					case DXEffectObject.STATE_TYPE.EXPRESSIONINDEX:
						shader = (DXShader) (((DXExpression)state.parameter.data)
							.Evaluate (_technique._effect.Parameters));
						break;
					case DXEffectObject.STATE_TYPE.PARAMETER:
						//should be easy, but haven't seen it
					default:
						throw new NotImplementedException();
					}
					
					if (shader.shaderType == ShaderType.FragmentShader) {
						if (shader != pixelShader) {
							if (pixelShader != null) {
								GL.DetachShader (shaderProgram, pixelShader.shader);
							}
							relink = true;
							pixelShader = shader;
							GL.AttachShader (shaderProgram, pixelShader.shader);
						}
					} else if (shader.shaderType == ShaderType.VertexShader) {
						if (shader != vertexShader) {
							if (vertexShader != null) {
								GL.DetachShader(shaderProgram, vertexShader.shader);
							}
							relink = true;
							vertexShader = shader;
							GL.AttachShader (shaderProgram, vertexShader.shader);
						}
					}
					
				}
				
			}
			
			if (relink) {
				Link ();
			}
			
			GL.UseProgram (shaderProgram);

			if (vertexShader != null) {
				vertexShader.Apply(shaderProgram,
				                  _technique._effect.Parameters,
				                  _graphicsDevice);
			} else {
				//passthrough shader is attached
				Viewport vp = _graphicsDevice.Viewport;
				Matrix projection = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);
				Matrix halfPixelOffset = Matrix.CreateTranslation(-0.5f, -0.5f, 0);
				Matrix transform = halfPixelOffset * projection;

				int uniform = GL.GetUniformLocation(shaderProgram, "transformMatrix");
				GL.UniformMatrix4(uniform, 1, false, Matrix.ToFloatArray(transform));
			}

			if (pixelShader != null) {
				pixelShader.Apply(shaderProgram,
				                  _technique._effect.Parameters,
				                  _graphicsDevice);
			}

		}
		
		public string Name { get { return name; } }
		
    }
}
