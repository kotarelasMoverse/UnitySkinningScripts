using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TestScript : MonoBehaviour
{
    public ComputeShader testComputeShader;

    private ComputeBuffer _buffer;

    public int size = 32;
    public int repetitions = 1000;

    private float[] data;

    private void Awake() {
        _buffer = new ComputeBuffer(size, sizeof(float));
    }
    // Start is called before the first frame update
    void Start()
    {   
        data = new float[size];
        ZeroData(data);
        // Debug.Log(string.Join(',', data));
        int kernel = testComputeShader.FindKernel("CSMain");
        testComputeShader.SetBuffer(kernel, "_Buffer", _buffer);
        testComputeShader.SetInt("_Repetitions", repetitions);
        testComputeShader.Dispatch(kernel, Mathf.CeilToInt(size / 512.0f), 1, 1);
        _buffer.GetData(data);
        // Debug.Log(string.Join(',', data));
    }

    // Update is called once per frame
    void Update()
    {   
        ZeroData(data);
        // Debug.Log(string.Join(',', data));
        int kernel = testComputeShader.FindKernel("CSMain");
        testComputeShader.SetBuffer(kernel, "_Buffer", _buffer);
        testComputeShader.SetInt("_Repetitions", repetitions);
        testComputeShader.Dispatch(kernel, Mathf.CeilToInt(size / 512.0f), 1, 1);
        _buffer.GetData(data);
        // Debug.Log(string.Join(',', data));
        
    }

    private void ZeroData(float[] data) {
        for (int i=0; i<data.Length; i++) {
            data[i] = 0;
        }
    }

    private void OnDestroy() {
        _buffer.Release();
    }
}
