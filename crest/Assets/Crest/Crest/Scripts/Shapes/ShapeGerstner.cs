﻿// Crest Ocean System

// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE)

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Crest
{
    /// <summary>
    /// Gerstner ocean waves.
    /// </summary>
    [ExecuteAlways]
    public partial class ShapeGerstner : MonoBehaviour, IFloatingOrigin
    {
        [Tooltip("The spectrum that defines the ocean surface shape. Assign asset of type Crest/Ocean Waves Spectrum.")]
        public OceanWaveSpectrum _spectrum;

        [Tooltip("If a spectrum will not change at runtime, set this true to calculate the wave data once on first update rather than each frame."), SerializeField]
        bool _spectrumIsStatic = true;

        [Tooltip("Wind direction (angle from x axis in degrees)"), Range(-180, 180)]
        public float _windDirectionAngle = 0f;
        public Vector2 WindDir => new Vector2(Mathf.Cos(Mathf.PI * _windDirectionAngle / 180f), Mathf.Sin(Mathf.PI * _windDirectionAngle / 180f));

        [Delayed, Tooltip("How many wave components to generate in each octave.")]
        public int _componentsPerOctave = 8;

        [Range(0f, 1f)]
        public float _weight = 1f;

        public int _randomSeed = 0;

        [Delayed]
        public int _resolution = 32;

        [SerializeField]
        Renderer _meshForDrawingWaves;

        [SerializeField]
        bool _debugDrawSlicesInEditor = false;

        public class GerstnerBatch : ILodDataInput
        {
            Material _material;
            Renderer _rend;

            RenderTexture _waveBuffer;
            int _waveBufferSliceIndex;

            public GerstnerBatch(float wavelength, RenderTexture waveBuffer, int waveBufferSliceIndex, Shader shaderGerstnerGlobal, Renderer renderer)
            {
                Wavelength = wavelength;
                _waveBuffer = waveBuffer;
                _waveBufferSliceIndex = waveBufferSliceIndex;
                _rend = renderer;

                if (_rend == null)
                {
                    _material = new Material(shaderGerstnerGlobal);
                }
                else
                {
                    _material = _rend.sharedMaterial;
                }
            }

            // The ocean input system uses this to decide which lod this batch belongs in
            public float Wavelength { get; private set; }

            public bool Enabled { get => true; set { } }

            public float Weight { get; set; }

            public void Draw(CommandBuffer buf, float weight, int isTransition, int lodIdx)
            {
                if (weight > 0f)
                {
                    buf.SetGlobalInt(LodDataMgr.sp_LD_SliceIndex, lodIdx);
                    buf.SetGlobalFloat(RegisterLodDataInputBase.sp_Weight, Weight * weight);
                    buf.SetGlobalTexture(sp_WaveBuffer, _waveBuffer);
                    buf.SetGlobalInt(sp_WaveBufferSliceIndex, _waveBufferSliceIndex);
                    buf.SetGlobalFloat(sp_AverageWavelength, Wavelength * 1.5f);

                    // Either use a full screen quad, or a provided mesh renderer to draw the waves
                    if (_rend == null)
                    {
                        buf.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
                    }
                    else
                    {
                        buf.DrawRenderer(_rend, _material);
                    }
                }
            }
        }

        const int CASCADE_COUNT = 16;
        const int MAX_WAVE_COMPONENTS = 1024;

        GerstnerBatch[] _batches = null;

        // Data for all components
        float[] _wavelengths;
        float[] _amplitudes;
        float[] _powers;
        float[] _angleDegs;
        float[] _phases;

        [HideInInspector]
        public RenderTexture _waveBuffers;

        struct GerstnerCascadeParams
        {
            public int _startIndex;
            public float _W_cumulative;
        }
        ComputeBuffer _bufCascadeParams;
        GerstnerCascadeParams[] _cascadeParams = new GerstnerCascadeParams[CASCADE_COUNT + 1];

        // First cascade of wave buffer that has waves and will be rendered
        int _firstCascade = -1;
        // Last cascade of wave buffer that has waves and will be rendered
        int _lastCascade = -1;

        // Used to populate data on first frame
        bool _firstUpdate = true;

        struct GerstnerWaveComponent4
        {
            public Vector4 _twoPiOverWavelength;
            public Vector4 _amp;
            public Vector4 _waveDirX;
            public Vector4 _waveDirZ;
            public Vector4 _omega;
            public Vector4 _phase;
            public Vector4 _chopAmp;
        }
        ComputeBuffer _bufWaveData;
        GerstnerWaveComponent4[] _waveData = new GerstnerWaveComponent4[MAX_WAVE_COMPONENTS / 4];

        ComputeShader _shaderGerstner;
        int _krnlGerstner = -1;

        readonly int sp_FirstCascadeIndex = Shader.PropertyToID("_FirstCascadeIndex");
        readonly int sp_TextureRes = Shader.PropertyToID("_TextureRes");
        readonly int sp_CascadeParams = Shader.PropertyToID("_GerstnerCascadeParams");
        readonly int sp_GerstnerWaveData = Shader.PropertyToID("_GerstnerWaveData");
        static readonly int sp_WaveBuffer = Shader.PropertyToID("_WaveBuffer");
        static readonly int sp_WaveBufferSliceIndex = Shader.PropertyToID("_WaveBufferSliceIndex");
        static readonly int sp_AverageWavelength = Shader.PropertyToID("_AverageWavelength");
        readonly int sp_AxisX = Shader.PropertyToID("_AxisX");

        readonly float _twoPi = 2f * Mathf.PI;
        readonly float _recipTwoPi = 1f / (2f * Mathf.PI);

        void InitData()
        {
            {
                _waveBuffers = new RenderTexture(_resolution, _resolution, 0, GraphicsFormat.R16G16B16A16_SFloat);
                _waveBuffers.wrapMode = TextureWrapMode.Clamp;
                _waveBuffers.antiAliasing = 1;
                _waveBuffers.filterMode = FilterMode.Bilinear;
                _waveBuffers.anisoLevel = 0;
                _waveBuffers.useMipMap = false;
                _waveBuffers.name = "GerstnerCascades";
                _waveBuffers.dimension = TextureDimension.Tex2DArray;
                _waveBuffers.volumeDepth = CASCADE_COUNT;
                _waveBuffers.enableRandomWrite = true;
                _waveBuffers.Create();
            }

            _bufCascadeParams = new ComputeBuffer(CASCADE_COUNT + 1, UnsafeUtility.SizeOf<GerstnerCascadeParams>());
            _bufWaveData = new ComputeBuffer(MAX_WAVE_COMPONENTS / 4, UnsafeUtility.SizeOf<GerstnerWaveComponent4>());

            _shaderGerstner = ComputeShaderHelpers.LoadShader("Gerstner");
            _krnlGerstner = _shaderGerstner.FindKernel("Gerstner");
        }

        /// <summary>
        /// Min wavelength for a cascade in the wave buffer. Does not depend on viewpoint.
        /// </summary>
        public float MinWavelength(int cascadeIdx)
        {
            var diameter = 0.5f * (1 << cascadeIdx);
            var texelSize = diameter / _resolution;
            return texelSize * OceanRenderer.Instance.MinTexelsPerWave;
        }

        public void CrestUpdate(CommandBuffer buf)
        {
            if (_waveBuffers == null || _resolution != _waveBuffers.width || _bufCascadeParams == null || _bufWaveData == null)
            {
                InitData();
            }

            var updateDataEachFrame = !_spectrumIsStatic;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying) updateDataEachFrame = true;
#endif
            if (_firstUpdate || updateDataEachFrame)
            {
                UpdateWaveData();

                InitBatches();

                _firstUpdate = false;
            }

            // Set weights - this should always happen
            foreach (var batch in _batches)
            {
                if (batch != null)
                {
                    batch.Weight = _weight;
                }
            }

            ReportMaxDisplacement();

            // If some cascades have waves in them, generate
            if (_firstCascade != -1 && _lastCascade != -1)
            {
                UpdateGenerateWaves(buf);
            }

            buf.SetGlobalVector(sp_AxisX, WindDir);
        }

        void SliceUpWaves()
        {
            float divider = 1f;

            _firstCascade = _lastCascade = -1;

            var cascadeIdx = 0;
            var componentIdx = 0;
            var outputIdx = 0;
            _cascadeParams[0]._startIndex = 0;
            _cascadeParams[0]._W_cumulative = 0f;

            // Seek forward to first wavelength that is big enough to render into current cascades
            var minWl = MinWavelength(cascadeIdx);
            while (componentIdx < _wavelengths.Length && _wavelengths[componentIdx] < minWl)
            {
                // Compute foam term
                float k = _twoPi / _wavelengths[componentIdx];
                float knext = _twoPi / _wavelengths[Mathf.Min(componentIdx + 1, _wavelengths.Length - 1)];
                float k2 = k * k;
                float specSample = _powers[componentIdx];
                float slopeVariance = k2 * specSample * specSample * Mathf.Abs(knext - k);
                float chopScale = _spectrum._chopScales[componentIdx / _componentsPerOctave];
                _cascadeParams[cascadeIdx]._W_cumulative += chopScale * slopeVariance / divider;

                componentIdx++;
            }
            //Debug.Log($"{cascadeIdx}: start {_cascadeParams[cascadeIdx]._startIndex} minWL {minWl}");

            for (; componentIdx < _wavelengths.Length; componentIdx++)
            {
                // Skip small amplitude waves
                while (componentIdx < _wavelengths.Length && _amplitudes[componentIdx] < 0.001f)
                {
                    // Compute foam term
                    float k = _twoPi / _wavelengths[componentIdx];
                    float knext = _twoPi / _wavelengths[Mathf.Min(componentIdx + 1, _wavelengths.Length - 1)];
                    float k2 = k * k;
                    float specSample = _powers[componentIdx];
                    float slopeVariance = k2 * specSample * specSample * Mathf.Abs(knext - k);
                    float chopScale = _spectrum._chopScales[componentIdx / _componentsPerOctave];
                    _cascadeParams[cascadeIdx]._W_cumulative += chopScale * slopeVariance / divider;

                    //Debug.Log("KSIP: " + (chopScale * slopeVariance / divider) + ", power: " + _powers[componentIdx]);
                    componentIdx++;
                }
                if (componentIdx >= _wavelengths.Length) break;

                // Check if we need to move to the next cascade
                while (cascadeIdx < CASCADE_COUNT && _wavelengths[componentIdx] >= 2f * minWl)
                {
                    // Wrap up this cascade and begin next

                    // Fill remaining elements of current vector4 with 0s
                    int vi = outputIdx / 4;
                    int ei = outputIdx - vi * 4;

                    while (ei != 0)
                    {
                        _waveData[vi]._twoPiOverWavelength[ei] = 1f;
                        _waveData[vi]._amp[ei] = 0f;
                        _waveData[vi]._waveDirX[ei] = 0f;
                        _waveData[vi]._waveDirZ[ei] = 0f;
                        _waveData[vi]._omega[ei] = 0f;
                        _waveData[vi]._phase[ei] = 0f;
                        _waveData[vi]._chopAmp[ei] = 0f;
                        ei = (ei + 1) % 4;
                        outputIdx++;
                    }

                    if (outputIdx > 0 && _firstCascade == -1) _firstCascade = cascadeIdx;

                    cascadeIdx++;
                    _cascadeParams[cascadeIdx]._startIndex = outputIdx / 4;
                    _cascadeParams[cascadeIdx]._W_cumulative = cascadeIdx > 0 ? _cascadeParams[cascadeIdx - 1]._W_cumulative : 0f;
                    minWl *= 2f;

                    //Debug.Log($"{cascadeIdx}: start {_cascadeParams[cascadeIdx]._startIndex} minWL {minWl}");
                }
                if (cascadeIdx == CASCADE_COUNT) break;

                {
                    // Pack into vector elements
                    int vi = outputIdx / 4;
                    int ei = outputIdx - vi * 4;

                    _waveData[vi]._twoPiOverWavelength[ei] = 2f * Mathf.PI / _wavelengths[componentIdx];
                    _waveData[vi]._amp[ei] = _amplitudes[componentIdx];

                    float chopScale = _spectrum._chopScales[componentIdx / _componentsPerOctave];
                    _waveData[vi]._chopAmp[ei] = -chopScale * _spectrum._chop * _amplitudes[componentIdx];

                    float angle = Mathf.Deg2Rad * _angleDegs[componentIdx];
                    float dx = Mathf.Cos(angle);
                    float dz = Mathf.Sin(angle);

                    // It used to be this, but I'm pushing all the stuff that doesn't depend on position into the phase.
                    //half4 angle = k * (C * _CrestTime + x) + _Phases[vi];
                    float gravityScale = _spectrum._gravityScales[(componentIdx) / _componentsPerOctave];
                    float gravity = OceanRenderer.Instance.Gravity * _spectrum._gravityScale;
                    float C = Mathf.Sqrt(_wavelengths[componentIdx] * gravity * gravityScale * _recipTwoPi);
                    float k = _twoPi / _wavelengths[componentIdx];

                    // Constrain wave vector (wavelength and wave direction) to ensure wave tiles across domain
                    {
                        float kx = k * dx;
                        float kz = k * dz;
                        var diameter = 0.5f * (1 << cascadeIdx);
                        float n = kx / (2f * Mathf.PI / diameter);
                        float m = kz / (2f * Mathf.PI / diameter);
                        kx = 2f * Mathf.PI * Mathf.Round(n) / diameter;
                        kz = 2f * Mathf.PI * Mathf.Round(m) / diameter;

                        k = Mathf.Sqrt(kx * kx + kz * kz);
                        dx = kx / k;
                        dz = kz / k;
                    }

                    _waveData[vi]._waveDirX[ei] = dx;
                    _waveData[vi]._waveDirZ[ei] = dz;

                    // Repeat every 2pi to keep angle bounded - helps precision on 16bit platforms
                    _waveData[vi]._omega[ei] = k * C;
                    _waveData[vi]._phase[ei] = Mathf.Repeat(_phases[componentIdx], Mathf.PI * 2f);

                    // Compute foam term
                    float k2 = k * k;
                    float knext = _twoPi / _wavelengths[Mathf.Min(componentIdx + 1, _wavelengths.Length - 1)];
                    float specSample = _powers[componentIdx];
                    float slopeVariance = k2 * specSample * specSample * Mathf.Abs(knext - k);
                    _cascadeParams[cascadeIdx]._W_cumulative += chopScale * slopeVariance / divider;

                    outputIdx++;
                }
            }

            _lastCascade = cascadeIdx;

            {
                // Fill remaining elements of current vector4 with 0s
                int vi = outputIdx / 4;
                int ei = outputIdx - vi * 4;

                while (ei != 0)
                {
                    _waveData[vi]._twoPiOverWavelength[ei] = 1f;
                    _waveData[vi]._amp[ei] = 0f;
                    _waveData[vi]._waveDirX[ei] = 0f;
                    _waveData[vi]._waveDirZ[ei] = 0f;
                    _waveData[vi]._omega[ei] = 0f;
                    _waveData[vi]._phase[ei] = 0f;
                    _waveData[vi]._chopAmp[ei] = 0f;
                    ei = (ei + 1) % 4;
                    outputIdx++;
                }
            }

            while (cascadeIdx < CASCADE_COUNT)
            {
                cascadeIdx++;
                minWl *= 2f;
                _cascadeParams[cascadeIdx]._startIndex = outputIdx / 4;
                _cascadeParams[cascadeIdx]._W_cumulative = cascadeIdx > 0 ? _cascadeParams[cascadeIdx - 1]._W_cumulative : 0f;
                //Debug.Log($"{cascadeIdx}: start {_cascadeParams[cascadeIdx]._startIndex} minWL {minWl}");
            }

            _lastCascade = CASCADE_COUNT - 1;

            //for (int i = 0; i < CASCADE_COUNT; i++)
            //{
            //    _cascadeParams[i]._W_cumulative = i > 0 ? _cascadeParams[i - 1]._W_cumulative : 0f;

            //    var wl = MinWavelength(i) * 1.5f;
            //    var octaveIndex = OceanWaveSpectrum.GetOctaveIndex(wl);
            //    //Debug.Log("Index: " + octaveIndex);
            //    octaveIndex = Mathf.Min(octaveIndex, _spectrum._chopScales.Length - 1);

            //    var amp = _spectrum.GetAmplitude(wl, 1f, out _);
            //    var chop = _spectrum._chopScales[octaveIndex];
            //    float amp_over_wl = chop * amp / wl;
            //    _cascadeParams[i]._W_cumulative += amp_over_wl;
            //}


            _bufCascadeParams.SetData(_cascadeParams);
            _bufWaveData.SetData(_waveData);
        }

        void UpdateGenerateWaves(CommandBuffer buf)
        {
            buf.SetComputeFloatParam(_shaderGerstner, sp_TextureRes, _waveBuffers.width);
            buf.SetComputeFloatParam(_shaderGerstner, OceanRenderer.sp_crestTime, OceanRenderer.Instance.CurrentTime);
            buf.SetComputeIntParam(_shaderGerstner, sp_FirstCascadeIndex, _firstCascade);
            buf.SetComputeBufferParam(_shaderGerstner, _krnlGerstner, sp_CascadeParams, _bufCascadeParams);
            buf.SetComputeBufferParam(_shaderGerstner, _krnlGerstner, sp_GerstnerWaveData, _bufWaveData);
            buf.SetComputeTextureParam(_shaderGerstner, _krnlGerstner, sp_WaveBuffer, _waveBuffers);

            buf.DispatchCompute(_shaderGerstner, _krnlGerstner, _waveBuffers.width / LodDataMgr.THREAD_GROUP_SIZE_X, _waveBuffers.height / LodDataMgr.THREAD_GROUP_SIZE_Y, _lastCascade - _firstCascade + 1);
        }

        public void SetOrigin(Vector3 newOrigin)
        {
            if (_phases == null) return;

            var windAngle = _windDirectionAngle;
            for (int i = 0; i < _phases.Length; i++)
            {
                var direction = new Vector3(Mathf.Cos((windAngle + _angleDegs[i]) * Mathf.Deg2Rad), 0f, Mathf.Sin((windAngle + _angleDegs[i]) * Mathf.Deg2Rad));
                var phaseOffsetMeters = Vector3.Dot(newOrigin, direction);

                // wave number
                var k = 2f * Mathf.PI / _wavelengths[i];

                _phases[i] = Mathf.Repeat(_phases[i] + phaseOffsetMeters * k, Mathf.PI * 2f);
            }
        }

        public void UpdateWaveData()
        {
            // Set random seed to get repeatable results
            Random.State randomStateBkp = Random.state;
            Random.InitState(_randomSeed);

            _spectrum.GenerateWaveData(_componentsPerOctave, ref _wavelengths, ref _angleDegs);

            UpdateAmplitudes();

            // Won't run every time so put last in the random sequence
            if (_phases == null || _phases.Length != _wavelengths.Length)
            {
                InitPhases();
            }

            Random.state = randomStateBkp;

            SliceUpWaves();
        }

        void UpdateAmplitudes()
        {
            if (_amplitudes == null || _amplitudes.Length != _wavelengths.Length)
            {
                _amplitudes = new float[_wavelengths.Length];
            }
            if (_powers == null || _powers.Length != _wavelengths.Length)
            {
                _powers = new float[_wavelengths.Length];
            }

            for (int i = 0; i < _wavelengths.Length; i++)
            {
                _amplitudes[i] = _weight * _spectrum.GetAmplitude(_wavelengths[i], _componentsPerOctave, out _powers[i]);
            }
        }

        void InitPhases()
        {
            // Set random seed to get repeatable results
            Random.State randomStateBkp = Random.state;
            Random.InitState(_randomSeed);

            var totalComps = _componentsPerOctave * OceanWaveSpectrum.NUM_OCTAVES;
            _phases = new float[totalComps];
            for (var octave = 0; octave < OceanWaveSpectrum.NUM_OCTAVES; octave++)
            {
                for (var i = 0; i < _componentsPerOctave; i++)
                {
                    var index = octave * _componentsPerOctave + i;
                    var rnd = (i + Random.value) / _componentsPerOctave;
                    _phases[index] = 2f * Mathf.PI * rnd;
                }
            }

            Random.state = randomStateBkp;
        }

        private void ReportMaxDisplacement()
        {
            if (_spectrum._chopScales.Length != OceanWaveSpectrum.NUM_OCTAVES)
            {
                Debug.LogError($"OceanWaveSpectrum {_spectrum.name} is out of date, please open this asset and resave in editor.", _spectrum);
            }

            float ampSum = 0f;
            for (int i = 0; i < _wavelengths.Length; i++)
            {
                ampSum += _amplitudes[i] * _spectrum._chopScales[i / _componentsPerOctave];
            }
            OceanRenderer.Instance.ReportMaxDisplacementFromShape(ampSum * _spectrum._chop, ampSum, ampSum);
        }

        void InitBatches()
        {
            var registered = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));

            //#if UNITY_EDITOR
            // Unregister after switching modes in the editor.
            if (_batches != null)
            {
                foreach (var batch in _batches)
                {
                    registered.Remove(batch);
                }
            }
            //#endif

            var shaderGerstnerGlobal = Shader.Find("Hidden/Crest/Inputs/Animated Waves/Gerstner Global");

            // Submit draws to create the Gerstner waves
            _batches = new GerstnerBatch[CASCADE_COUNT];
            for (int i = _firstCascade; i <= _lastCascade; i++)
            {
                if (i == -1) break;
                _batches[i] = new GerstnerBatch(MinWavelength(i), _waveBuffers, i, shaderGerstnerGlobal, _meshForDrawingWaves);
                registered.Add(0, _batches[i]);
            }
        }

        private void OnEnable()
        {
            _firstUpdate = true;

#if UNITY_EDITOR
            // Initialise with spectrum
            if (_spectrum == null)
            {
                _spectrum = ScriptableObject.CreateInstance<OceanWaveSpectrum>();
                _spectrum.name = "Default Waves (auto)";
            }

            if (EditorApplication.isPlaying && !Validate(OceanRenderer.Instance, ValidatedHelper.DebugLog))
            {
                enabled = false;
                return;
            }

            _spectrum.Upgrade();
#endif

            LodDataMgrAnimWaves.RegisterUpdatable(this);
        }

        void OnDisable()
        {
            LodDataMgrAnimWaves.DeregisterUpdatable(this);

            if (_batches != null)
            {
                var registered = RegisterLodDataInputBase.GetRegistrar(typeof(LodDataMgrAnimWaves));
                foreach (var batch in _batches)
                {
                    registered.Remove(batch);
                }

                _batches = null;
            }

            if (_bufCascadeParams != null && _bufCascadeParams.IsValid())
            {
                _bufCascadeParams.Dispose();
                _bufCascadeParams = null;
            }
            if (_bufWaveData != null && _bufWaveData.IsValid())
            {
                _bufWaveData.Dispose();
                _bufWaveData = null;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf)
            {
                Gizmos.color = RegisterAnimWavesInput.s_gizmoColor;
                Gizmos.DrawWireMesh(mf.sharedMesh, transform.position, transform.rotation, transform.lossyScale);
            }
        }

        void OnGUI()
        {
            if (_debugDrawSlicesInEditor)
            {
                OceanDebugGUI.DrawTextureArray(_waveBuffers, 8);
            }
        }
#endif
    }

#if UNITY_EDITOR
    public partial class ShapeGerstner : IValidated
    {
        public bool Validate(OceanRenderer ocean, ValidatedHelper.ShowMessage showMessage)
        {
            var isValid = true;

            if (_spectrum == null)
            {
                showMessage
                (
                    "There is no spectrum assigned meaning this Gerstner component won't generate any waves.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            if (_componentsPerOctave == 0)
            {
                showMessage
                (
                    "Components Per Octave set to 0 meaning this Gerstner component won't generate any waves.",
                    ValidatedHelper.MessageType.Warning, this
                );

                isValid = false;
            }

            return isValid;
        }
    }
#endif
}