using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity.Mathematics;
using System.Linq;
using System.Runtime.InteropServices;

public class LinearBlendSkinningWithOffsets : MonoBehaviour
{
    [Range(-180, 180)]
    public int LHipXAngle;
    [Range(-180, 180)]
    public int LKneeXAngle;
    [Range(-180, 180)]
    public int LAnkleXAngle;
    [Range(-180, 180)]
    public int RShoulderZAngle;
    [Range(-180, 180)]
    public int RElbowYAngle;
    public bool useGPU = false;
    public ComputeShader LinearBlendSkinningComputeShader;
    private ComputeBuffer _verticesBuffer;
    ComputeBuffer animatedJointPositionsBuffer;
    // ComputeBuffer verticesInfoBuffer;
    ComputeBuffer verticesOffsetsAtTPoseBuffer;
    ComputeBuffer influenceJointsIdsPerVertexBuffer;
    ComputeBuffer skinningWeightsPerVertexBuffer;
    ComputeBuffer influenceJointsNumberPerVertexBuffer;
    ComputeBuffer jointsAccumRotationsBuffer;

    GameObject[] skeletonWithoutHierarchy;
    GameObject skeletonMeshWithoutHierarchy;
    GameObject skeletonWithHierarchy;
    GameObject skeletonMeshWithHierarchy;
    Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone;
    Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsets;
    Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo;
    
    int numberOfVertices;
    Transform[] inputBones;

    List<int[]> paths = new List<int[]>
        {
            new int[] { 0, 1, 4, 7, 10 },
            new int[] { 0, 2, 5, 8, 11 },
            new int[] { 0, 3, 6, 9, 12, 15 },
            new int[] { 0, 3, 6, 9, 13, 16, 18, 20, 22 },
            new int[] { 0, 3, 6, 9, 14, 17, 19, 21, 23 }
        };

    Dictionary<int, Vector3> animationParams;
    
    // struct VertexShaderInfoStruct {
    //     public Vector4[] offsetsAtTPose;
    //     public int[] influenceJointsIds;
    //     public float[] skinningWeights;
    //     public int influenceJointsNumber;
    // }

    // Start is called before the first frame update
    void Start()
    {
        GameObject inputModel = GameObject.Find("Clone");
        Transform skeletonTransform = inputModel.transform.GetChild(0); // The transform of the skeleton game object
        Transform root = skeletonTransform.GetChild(0); // The transform of the root joint (pelvis) game object
        SkinnedMeshRenderer rend = root.GetComponentInParent<SkinnedMeshRenderer>();

        // Set the model to T-pose
        Utils.SetModelToTPose(root);

        // Extract vertices position in T-pose, the position are in local coordinate system with center the center of the mesh (average of all the vertices positions and position of the mesh object)
        Mesh inputMesh = rend.sharedMesh;
        Vector3[] inputVerticesLocalCoord = inputMesh.vertices;
        numberOfVertices = inputVerticesLocalCoord.Length;
        Debug.Log("Clone mesh vertex number " + numberOfVertices);

        // Get center of mesh in world coordinates
        Vector3 inputMeshCenter = new Vector3();
        for (int i=0; i<inputVerticesLocalCoord.Length; i++) {
            inputMeshCenter += rend.localToWorldMatrix.MultiplyPoint(inputVerticesLocalCoord[i]);
        }
        inputMeshCenter = inputMeshCenter / inputVerticesLocalCoord.Length;
        Debug.Log("Clone mesh center in world coordinates" + inputMeshCenter);
        Debug.Log("Root position " + root.position);

        // Get the positions of the vertices in T-pose, in the world coordinate system
        Vector3[] inputVerticesWorldCoord = new Vector3[inputVerticesLocalCoord.Length];
        Matrix4x4 transfMatrix = rend.localToWorldMatrix;
        for (int i=0; i<inputVerticesLocalCoord.Length; i++) {
            inputVerticesWorldCoord[i] = transfMatrix.MultiplyPoint(inputVerticesLocalCoord[i]);
        }

        // Get the positions of the vertices in T-pose in the root joint coordinate system
        Vector3[] inputVerticesRootCoord = new Vector3[inputVerticesWorldCoord.Length];
        for (int i=0; i<inputVerticesWorldCoord.Length; i++) {
            inputVerticesRootCoord[i] = inputVerticesWorldCoord[i] - root.position;
        }

        // Get list of joints/bones in T-pose, with the index of the list corresponding to the id of the bone
        inputBones = rend.bones;

        // Get bone weights for the vertices 
        var inputBoneWeights = inputMesh.GetAllBoneWeights();
        var inputBonesPerVertex = inputMesh.GetBonesPerVertex();

        // For each vertex get the infuence bones and the corresponding weights.
        vertexSkinningInfo = Utils.GetVertexSkinningInfo(inputBoneWeights, inputBonesPerVertex);

        // Hypothetical animation parameters
        animationParams = new Dictionary<int, Vector3>(); 
        animationParams[1] = new Vector3(45, 0, 0); // Rotate L Hip around x by 45 degrees
        animationParams[4] = new Vector3(45, 0, 0); // Rotate L Knee around x by 60 degrees
        animationParams[7] = new Vector3(45, 0, 0); // Rotate L Ankle around x by 45 degrees
        animationParams[17] = new Vector3(0, 0, -30); // Rotate R shoulder around x by 90 degrees
        animationParams[19] = new Vector3(0, -100, 0); // Rotate R elbow around y by 90 degrees

        #region Without Hierarchy Skeleton
        // Create a skeleton without hierarchy, just a list of game objects
        skeletonWithoutHierarchy = Utils.BuildSkeletonWithoutHierarchy(inputBones);

        // Get the offsets between the vertices and the influence bones, in T-pose
        // verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPose(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);
        verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPoseInWorldSpcae(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);

        // Create a new mesh at the position of the skeleton without hierarchy
        skeletonMeshWithoutHierarchy = Utils.CreateNewMeshAtPosition(skeletonWithoutHierarchy[0].transform, inputVerticesRootCoord); // Pass the vertices position wrt the root bone (pelvis), so the vertices of the new mesh are also in the coordinate system of the root bone
        skeletonMeshWithoutHierarchy.name = "LinearBlendSkinningMeshNoHierarchy";

        // Apply the animation parameters to the skeleton without hierarchy
        Utils.AnimateSkeletonWithoutHierarchy(skeletonWithoutHierarchy, animationParams, inputBones, paths);

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

        // Apply simple skinning to the skeleton without hierarchy
        // Vector3[] linearBlendSkinnedSkeletonMeshVertices = LinearBlendSkinningFromOffsetsWithoutHierarchy(skeletonWithoutHierarchy, verticesAndJointsOffsets, vertexSkinningInfo);
        Vector3[] linearBlendSkinnedSkeletonMeshVertices = LinearBlendSkinningFromOffsetsWithoutHierarchyInWorldSpace(skeletonWithoutHierarchy, verticesAndJointsOffsets, vertexSkinningInfo, jointsAccumulatedRotations);
        skeletonMeshWithoutHierarchy.GetComponent<MeshFilter>().mesh.vertices = linearBlendSkinnedSkeletonMeshVertices;
        #endregion

        #region With Hierarchy Skeleton 
        // Create a skeleton with hierarchy
        skeletonWithHierarchy = Utils.BuildSkeletonWithHierarchy(root, null);

        // Associate the bones of the skeleton with hierarchy, with the boneIds
        boneIdToSkeletonWithHierarchyBone = new Dictionary<int, Transform>();
        Transform[] skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
        for (int b=0; b<inputBones.Length; b++) {
            int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
            boneIdToSkeletonWithHierarchyBone[b] = skeletonWithHierarchyArray[boneId];
        } 

        // Create a new mesh at the position of skeleton with hierarchy
        skeletonMeshWithHierarchy = Utils.CreateNewMeshAtPosition(skeletonWithHierarchy.transform, inputVerticesRootCoord);
        skeletonMeshWithHierarchy.name = "LinearBlendSkinningMeshHierarchy";

        // Apply the animation parameters to the skeleton with hierarchy
        Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

        // Vector3[] linearBlendSkinnedSkeletonMeshVerticesWithHierarchy = LinearBlendSkinningFromOffsetsWithHierarchy(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, vertexSkinningInfo);
        Vector3[] linearBlendSkinnedSkeletonMeshVerticesWithHierarchy = LinearBlendSkinningFromOffsetsWithHierarchyInWorldSpace(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, vertexSkinningInfo, jointsAccumulatedRotations);
        skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = linearBlendSkinnedSkeletonMeshVerticesWithHierarchy;
        #endregion

        #region GPU initializations
        // Implement for 4 infuence joints max
        // Prepare data and compute buffers with static data
        
        Vector4[] verticesOffsetsAtTPoseForShader = new Vector4[numberOfVertices*4];
        int[] influenceJointsIdsPerVertexForShader = new int[numberOfVertices*4];
        float[] skinningWeightsPerVertexForShader = new float[numberOfVertices*4];
        int[] influenceJointsNumberPerVertexForShader = new int[numberOfVertices];

        for (int vid=0; vid<numberOfVertices; vid++) {

            influenceJointsNumberPerVertexForShader[vid] = vertexSkinningInfo[vid].bonesIds.Length;
            
            for (int j=0; j<4; j++) {  //j<vertexSkinningInfo[vid].bonesIds.Length for all the available influence joints

                int influenceJointId = vertexSkinningInfo[vid].bonesIds[j];

                influenceJointsIdsPerVertexForShader[vid*4 + j] = influenceJointId;

                skinningWeightsPerVertexForShader[vid*4 + j] = vertexSkinningInfo[vid].weights[j];

                verticesOffsetsAtTPoseForShader[vid*4 + j] = verticesAndJointsOffsets[vid][influenceJointId];
            }
        }

        // verticesOffsetsInTPoseForShader = new Vector4[numberOfVertices];
        // idsOfInfluenceJointsForShader = new int[numberOfVertices];
        // for (int vid=0; vid<numberOfVertices; vid++) {
        //     int influenceBoneId = verticesAndJointsOffsets[vid].Keys.ToList()[0];
        //     verticesOffsetsInTPoseForShader[vid] = verticesAndJointsOffsets[vid][influenceBoneId];
        //     idsOfInfluenceJointsForShader[vid] = influenceBoneId;
        // }
        // Initialize the vertices buffer
        _verticesBuffer = new ComputeBuffer(numberOfVertices*4, sizeof(float));

        animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));

        verticesOffsetsAtTPoseBuffer = new ComputeBuffer(numberOfVertices*16, sizeof(float));
        verticesOffsetsAtTPoseBuffer.SetData(verticesOffsetsAtTPoseForShader);

        influenceJointsIdsPerVertexBuffer = new ComputeBuffer(numberOfVertices*4, sizeof(int));
        influenceJointsIdsPerVertexBuffer.SetData(influenceJointsIdsPerVertexForShader);

        skinningWeightsPerVertexBuffer = new ComputeBuffer(numberOfVertices*4, sizeof(float));
        skinningWeightsPerVertexBuffer.SetData(skinningWeightsPerVertexForShader);

        influenceJointsNumberPerVertexBuffer = new ComputeBuffer(numberOfVertices, sizeof(int));
        influenceJointsNumberPerVertexBuffer.SetData(influenceJointsNumberPerVertexForShader);

        jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
        #endregion
    }

    // Update is called once per frame
    void Update()
    {
        // Simulate input animation parameters
        animationParams[1] = new Vector3(LHipXAngle, 0, 0); // Rotate L Hip around x by 45 degrees
        animationParams[4] = new Vector3(LKneeXAngle, 0, 0); // Rotate L Knee around x by 60 degrees
        animationParams[7] = new Vector3(LAnkleXAngle, 0, 0); // Rotate L Ankle around x by 45 degrees
        animationParams[17] = new Vector3(0, 0, RShoulderZAngle); // Rotate R shoulder around x by 90 degrees
        animationParams[19] = new Vector3(0, RElbowYAngle, 0); // Rotate R elbow around y by 90 degrees

        if (!useGPU) {
            // Real time skinning for the skeleton with hierarchy in CPU
            Utils.SetModelToTPose(skeletonWithHierarchy.transform);
            Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            // Vector3[] linearBlendSkinnedSkeletonMeshVerticesWithHierarchy = LinearBlendSkinningFromOffsetsWithHierarchy(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, vertexSkinningInfo);
            Vector3[] linearBlendSkinnedSkeletonMeshVerticesWithHierarchy = LinearBlendSkinningFromOffsetsWithHierarchyInWorldSpace(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, vertexSkinningInfo, jointsAccumulatedRotations);
            skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = linearBlendSkinnedSkeletonMeshVerticesWithHierarchy;
        }
        else {
            Utils.SetModelToTPose(skeletonWithHierarchy.transform);
            Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            // Get the animated joint positions in an array of Vector4s
            Vector4[] animatedJointPositionsForShader = new Vector4[inputBones.Length];
            Transform[] skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
            for (int b=0; b<inputBones.Length; b++) {
                int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                animatedJointPositionsForShader[b] = skeletonWithHierarchyArray[boneId].position;
            }

            // ComputeBuffer animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));
            animatedJointPositionsBuffer.SetData(animatedJointPositionsForShader);

            // ComputeBuffer jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
            jointsAccumRotationsBuffer.SetData(jointsAccumulatedRotations);

            float[] flattenedVertices = new float[numberOfVertices*4];

            int kernel = LinearBlendSkinningComputeShader.FindKernel("LinearBlendSkinning");
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_VertexBuffer", _verticesBuffer);
            LinearBlendSkinningComputeShader.SetInt("_NumVertices", Mathf.CeilToInt(numberOfVertices));
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_AnimatedJointPositions", animatedJointPositionsBuffer);
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_JointsAccumulatedRotations", jointsAccumRotationsBuffer);
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_VerticesOffsetsAtTPose", verticesOffsetsAtTPoseBuffer);
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_InfluenceJointsIdsPerVertex", influenceJointsIdsPerVertexBuffer);
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_SkinningWeightsPerVertex", skinningWeightsPerVertexBuffer);
            LinearBlendSkinningComputeShader.SetBuffer(kernel, "_InfluenceJointsNumberPerVertex", influenceJointsNumberPerVertexBuffer);


            LinearBlendSkinningComputeShader.Dispatch(kernel, Mathf.CeilToInt(numberOfVertices / 1024.0f), 1, 1);
            _verticesBuffer.GetData(flattenedVertices);

            // From flattened to vector3 array
            Vector3[] newVertices = new Vector3[numberOfVertices];
            for (int vid=0; vid<numberOfVertices; vid++) {
                newVertices[vid] = new Vector3(flattenedVertices[vid*4], flattenedVertices[vid*4 + 1], flattenedVertices[vid*4 + 2]);
            }
            
            skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = newVertices;
        }
    }

    public Vector3[] LinearBlendSkinningFromOffsetsWithoutHierarchy(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (skeleton[influenceBoneId].transform.TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position - skeleton[0].transform.position);
            }

        }

        return newVerticesPositions;
    }

    public Vector3[] LinearBlendSkinningFromOffsetsWithoutHierarchyInWorldSpace(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo, Matrix4x4[] jointIdToAccumulatedRotations) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (jointIdToAccumulatedRotations[influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position);
            }

        }

        return newVerticesPositions;
    }

    public Vector3[] LinearBlendSkinningFromOffsetsWithHierarchy(Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (boneIdToSkeletonWithHierarchyBone[influenceBoneId].TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + boneIdToSkeletonWithHierarchyBone[influenceBoneId].position - boneIdToSkeletonWithHierarchyBone[0].position);
            }

        }

        return newVerticesPositions;
    }

    public Vector3[] LinearBlendSkinningFromOffsetsWithHierarchyInWorldSpace(Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo, Matrix4x4[] jointIdToAccumulatedRotations) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (jointIdToAccumulatedRotations[influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + boneIdToSkeletonWithHierarchyBone[influenceBoneId].position);
            }

        }

        return newVerticesPositions;
    }

    // void OnApplicationQuit()
    // {
    //     _verticesBuffer.Release();
    //     animatedJointPositionsBuffer.Release();
    //     verticesOffsetsAtTPoseBuffer.Release();
    //     influenceJointsIdsPerVertexBuffer.Release();
    //     skinningWeightsPerVertexBuffer.Release();
    //     influenceJointsNumberPerVertexBuffer.Release();
    //     jointsAccumRotationsBuffer.Release();
    // }
}
