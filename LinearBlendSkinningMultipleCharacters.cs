using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UIElements;
using Unity.VisualScripting;
using Unity.Mathematics;

public class LinearBlendSkinningMultipleCharacters : MonoBehaviour
{   
    public int numberOfCharacters=1;
    public bool useGPU = false;
    Transform animatedModelRootDance;
    Transform animatedModelRootKobe;
    Transform animatedModelRootZeimpekiko;
    int numberOfVertices;
    Transform[] inputBones;
    Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo;
    GameObject skeletonWithHierarchy;
    GameObject[] skeletons;
    GameObject skeletonMeshWithHierarchy;
    GameObject[] skeletonMeshes;
    Transform[] skeletonWithHierarchyArray;
    Vector3[] initialSkeletonsPositions;
    Dictionary<int, Transform>[] boneIdToSkeletonWithHierarchyBone;
    Dictionary<int, int> hierarchyBoneIdToSkeletonArrayBoneId;
    Dictionary<int, int> skeletonArrayBoneIdToHierarchyBoneId;
    Dictionary<int, Vector3> animationParamsDance;
    Dictionary<int, Vector3> animationParamsKobe;
    Dictionary<int, Vector3> animationParamsZeimpekiko;
    Matrix4x4[] jointsAccumulatedRotations;
    Matrix4x4[][] allSkeletonsJointsAccumulatedRotations;
    Matrix4x4[] allSkeletonsJointsAccumulatedRotations1D;
    Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsets;

    public ComputeShader LinearBlendSkinningMultipleCharactersComputeShader;
    int kernelId;
    private ComputeBuffer _verticesBuffer;
    ComputeBuffer animatedJointPositionsBuffer;
    // ComputeBuffer verticesInfoBuffer;
    ComputeBuffer verticesOffsetsAtTPoseBuffer;
    ComputeBuffer influenceJointsIdsPerVertexBuffer;
    ComputeBuffer skinningWeightsPerVertexBuffer;
    ComputeBuffer jointsAccumRotationsBuffer;

    List<int[]> paths = new List<int[]>
        {
            new int[] { 0, 1, 4, 7, 10 },
            new int[] { 0, 2, 5, 8, 11 },
            new int[] { 0, 3, 6, 9, 12, 15 },
            new int[] { 0, 3, 6, 9, 13, 16, 18, 20, 22 },
            new int[] { 0, 3, 6, 9, 14, 17, 19, 21, 23 }
        };

    int NUMBER_OF_INFLUENCE_JOINTS = 4;
    Material customMaterial;

    // Start is called before the first frame update
    void Start()
    {
        Transform root = GameObject.Find("Clone").transform.GetChild(0).GetChild(0); // The transform of the root joint (pelvis) game object. From this we will create the rest of the characters.
        SkinnedMeshRenderer rend = root.GetComponentInParent<SkinnedMeshRenderer>(); // The skinned mesh renderer of the "Clone character"
        GameObject.Find("Clone").SetActive(false);

        animatedModelRootDance = GameObject.Find("Free-Dance-Movements").transform.GetChild(0).GetChild(0);
        animatedModelRootKobe = GameObject.Find("Kobe").transform.GetChild(0).GetChild(0).GetChild(0);
        animatedModelRootZeimpekiko = GameObject.Find("Zeimpekiko").transform.GetChild(0).GetChild(0).GetChild(0);

        // Set the reference model to T-pose
        Utils.SetModelToTPose(root);
        
        // Extract vertices position in T-pose, the position are in local coordinate system with center the center of the mesh (average of all the vertices positions and position of the mesh object)
        Mesh inputMesh = rend.sharedMesh;
        Vector3[] inputVerticesLocalCoord = inputMesh.vertices;
        numberOfVertices = inputVerticesLocalCoord.Length;
        Debug.Log("Clone mesh vertex number " + numberOfVertices);

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

        // Create a skeleton without hierarchy, just a list of game objects
        GameObject[] skeletonWithoutHierarchy = Utils.BuildSkeletonWithoutHierarchy(inputBones);

        // Get the offsets between the vertices and the influence bones, in T-pose
        // verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPose(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);
        verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPoseInWorldSpcae(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);

        foreach(GameObject o in skeletonWithoutHierarchy) { Destroy(o); }

        // Create all the character models' skeletons
        skeletons = new GameObject[numberOfCharacters];
        initialSkeletonsPositions = new Vector3[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) {
            skeletons[i] = Utils.BuildSkeletonWithHierarchy(root, null);
            // skeletons[i].transform.position += new Vector3(i*2, 0, 0);
            // initialSkeletonsPositions[i] = skeletons[i].transform.position;
        }
        

        // Associate the bones of the skeleton with hierarchy, with the boneIds
        // boneIdToSkeletonWithHierarchyBone = new Dictionary<int, Transform>[numberOfCharacters];
        skeletonWithHierarchyArray = skeletons[0].GetComponentsInChildren<Transform>();
        // for (int c=0; c<numberOfCharacters; c++) {
        //     boneIdToSkeletonWithHierarchyBone[c] = new Dictionary<int, Transform>();

        //     for (int b=0; b<inputBones.Length; b++) {
        //         int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
        //         boneIdToSkeletonWithHierarchyBone[b] = skeletonWithHierarchyArray[boneId];
        //     }
        // }
        

        // Associate the joint ids of the skeleton hierarchy, with the joint ids of the skeleton transforms array. (In the array the joints are not in the proper order)
        hierarchyBoneIdToSkeletonArrayBoneId = new Dictionary<int, int>();
        // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
        for (int b=0; b<inputBones.Length; b++) {
            int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
            hierarchyBoneIdToSkeletonArrayBoneId[b] = boneId;
        }

        // Associate the joint ids of the skeleton transforms array, with the joint ids of the skeleton hierarchy. The reverse of the previous (In the array the joints are not in the proper order)
        skeletonArrayBoneIdToHierarchyBoneId = new Dictionary<int, int>();
        // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
        for (int b=0; b<inputBones.Length; b++) {
            int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
            skeletonArrayBoneIdToHierarchyBoneId[boneId] = b;
        }

        // Create new meshes at the positions of skeletons
        skeletonMeshes = new GameObject[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) {
            skeletonMeshes[i] = Utils.CreateNewMeshAtPosition(skeletons[i].transform, inputVerticesWorldCoord);
            skeletonMeshes[i].name = "LinearBlendSkinningMeshCharacter" + $"{i}";
        }

        for (int i=0; i<numberOfCharacters; i++) {
            skeletons[i].transform.position += new Vector3(i*2, 0, 0);
            initialSkeletonsPositions[i] = skeletons[i].transform.position;
        }

        customMaterial = Instantiate(Resources.Load("CustomUnlitMaterial")) as Material;
        for (int sk=0; sk<numberOfCharacters; sk++) {
            skeletonMeshes[sk].GetComponent<MeshRenderer>().material = customMaterial;
            skeletonMeshes[sk].GetComponent<MeshRenderer>().material.SetInteger("_ModelID", sk);
        }
        // skeletonMeshes[0].GetComponent<MeshRenderer>().material = customMaterial;
        // skeletonMeshes[0].GetComponent<MeshRenderer>().material.SetInteger("_ModelID", 0);
        // skeletonMeshes[1].GetComponent<MeshRenderer>().material = customMaterial;
        // skeletonMeshes[1].GetComponent<MeshRenderer>().material.SetInteger("_ModelID", 1);

        // Extract the animation parameters from the character model that is moved by the animator
        animationParamsDance = ExtractAnimationParametersFromAnimatedModel(animatedModelRootDance, skeletonArrayBoneIdToHierarchyBoneId);
        animationParamsKobe = ExtractAnimationParametersFromAnimatedModel(animatedModelRootKobe, skeletonArrayBoneIdToHierarchyBoneId);
        animationParamsZeimpekiko = ExtractAnimationParametersFromAnimatedModel(animatedModelRootZeimpekiko, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Vector3>[] allAnimationParams = new Dictionary<int, Vector3>[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) { 
            if (i < 1) {
                allAnimationParams[i] = animationParamsDance;
            }
            else if (i < 3) {
                allAnimationParams[i] = animationParamsKobe;
            }
            else {
                allAnimationParams[i] = animationParamsZeimpekiko;
            }
             
        }


        // Apply the animation parameters to the skeleton with hierarchy
        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        allSkeletonsJointsAccumulatedRotations = Utils.AnimateMultipleSkeletonsWithHierarchy(skeletons, allAnimationParams, hierarchyBoneIdToSkeletonArrayBoneId, paths);

        // Get the animated joint positions in an array of Vector4s
        Vector3[][] animatedJointPositions = new Vector3[skeletons.Length][]; //
        // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
        for (int sk=0; sk<skeletons.Length; sk++) {
            animatedJointPositions[sk] = new Vector3[inputBones.Length];
            skeletonWithHierarchyArray = skeletons[sk].transform.GetComponentsInChildren<Transform>();

            for (int b=0; b<inputBones.Length; b++) {
                // int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                animatedJointPositions[sk][b] = skeletonWithHierarchyArray[hierarchyBoneIdToSkeletonArrayBoneId[b]].position;
            }
        }

        Vector3[][] linearBlendSkinnedSkeletonMeshesVerticesWithHierarchy = LinearBlendSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(animatedJointPositions, verticesAndJointsOffsets, vertexSkinningInfo, allSkeletonsJointsAccumulatedRotations);
        for (int sk=0; sk<skeletons.Length; sk++) {
            skeletonMeshes[sk].GetComponent<MeshFilter>().mesh.vertices = linearBlendSkinnedSkeletonMeshesVerticesWithHierarchy[sk];
        }

        Vector3[] flattenedVertices = new Vector3[numberOfCharacters * numberOfVertices];
        for (int sk=0; sk<skeletons.Length; sk++) {
            for (int vid=0; vid<numberOfVertices; vid++){
                flattenedVertices[vid + sk * numberOfVertices] = linearBlendSkinnedSkeletonMeshesVerticesWithHierarchy[sk][vid];
            }
        }
        
        #region GPU initializations
        // Implement for 4 infuence joints max
        // Prepare data and compute buffers with static data
        
        Vector4[] verticesOffsetsAtTPoseForShader = new Vector4[numberOfVertices * NUMBER_OF_INFLUENCE_JOINTS * numberOfCharacters];
        int[] influenceJointsIdsPerVertexForShader = new int[numberOfVertices * NUMBER_OF_INFLUENCE_JOINTS];
        float[] skinningWeightsPerVertexForShader = new float[numberOfVertices * NUMBER_OF_INFLUENCE_JOINTS];
        
        for (int vid=0; vid<numberOfVertices; vid++) {
                
            for (int j=0; j<NUMBER_OF_INFLUENCE_JOINTS; j++) {  //j<vertexSkinningInfo[vid].bonesIds.Length for all the available influence joints

                    int influenceJointId = vertexSkinningInfo[vid].bonesIds[j];

                    influenceJointsIdsPerVertexForShader[vid*4 + j] = influenceJointId;

                    skinningWeightsPerVertexForShader[vid*4 + j] = vertexSkinningInfo[vid].weights[j];

                for (int sk=0; sk<numberOfCharacters; sk++) {

                    verticesOffsetsAtTPoseForShader[vid*4 + j + sk * numberOfVertices * 4] = verticesAndJointsOffsets[vid][influenceJointId];
                }
            }
        }

        kernelId = LinearBlendSkinningMultipleCharactersComputeShader.FindKernel("LinearBlendSkinningMultipleCharacters");
        LinearBlendSkinningMultipleCharactersComputeShader.SetInt("_NumVertices", numberOfVertices);
        LinearBlendSkinningMultipleCharactersComputeShader.SetInt("_NumCharacters", numberOfCharacters);
        LinearBlendSkinningMultipleCharactersComputeShader.SetInt("_NumJoints", inputBones.Length);
        LinearBlendSkinningMultipleCharactersComputeShader.SetInt("_NumInfluenceJoints", NUMBER_OF_INFLUENCE_JOINTS);

        // The 4's are because we use Vector4. The 16 at the end is because we use a Matrix4x4
        // Initialize the vertices buffer
        _verticesBuffer = new ComputeBuffer(numberOfVertices * 4 * numberOfCharacters, sizeof(float));
        LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_VertexBuffer", _verticesBuffer);
        for (int sk=0; sk<numberOfCharacters; sk++) {
            skeletonMeshes[sk].GetComponent<MeshRenderer>().material.SetBuffer("_NewVertexPosBuffer", _verticesBuffer);
        }
        // skeletonMeshes[0].GetComponent<MeshRenderer>().material.SetBuffer("_NewVertexPosBuffer", _verticesBuffer);
        // skeletonMeshes[1].GetComponent<MeshRenderer>().material.SetBuffer("_NewVertexPosBuffer", _verticesBuffer);

        animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length * 4 * numberOfCharacters, sizeof(float));

        verticesOffsetsAtTPoseBuffer = new ComputeBuffer(numberOfVertices * 4 * NUMBER_OF_INFLUENCE_JOINTS * numberOfCharacters, sizeof(float));
        verticesOffsetsAtTPoseBuffer.SetData(verticesOffsetsAtTPoseForShader);
        LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_VerticesOffsetsAtTPose", verticesOffsetsAtTPoseBuffer);

        influenceJointsIdsPerVertexBuffer = new ComputeBuffer(numberOfVertices * NUMBER_OF_INFLUENCE_JOINTS, sizeof(int));
        influenceJointsIdsPerVertexBuffer.SetData(influenceJointsIdsPerVertexForShader);
        LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_InfluenceJointsIdsPerVertex", influenceJointsIdsPerVertexBuffer);

        skinningWeightsPerVertexBuffer = new ComputeBuffer(numberOfVertices * NUMBER_OF_INFLUENCE_JOINTS, sizeof(float));
        skinningWeightsPerVertexBuffer.SetData(skinningWeightsPerVertexForShader);
        LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_SkinningWeightsPerVertex", skinningWeightsPerVertexBuffer);

        jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length * 16 * numberOfCharacters, sizeof(float));
        #endregion
    }

    // Update is called once per frame
    void Update()
    {
        // Extract the animation parameters from the character model that is moved by the animator
        Dictionary<int, Quaternion> animationParamsDance = ExtractAnimationParametersFromAnimatedModelInQuaternions(animatedModelRootDance, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Quaternion> animationParamsKobe = ExtractAnimationParametersFromAnimatedModelInQuaternions(animatedModelRootKobe, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Quaternion> animationParamsZeimpekiko = ExtractAnimationParametersFromAnimatedModelInQuaternions(animatedModelRootZeimpekiko, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Quaternion>[] allAnimationParams = new Dictionary<int, Quaternion>[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) { 
            if (i < 1) {
                allAnimationParams[i] = animationParamsDance;
            }
            else if (i < 3) {
                allAnimationParams[i] = animationParamsKobe;
            }
            else {
                allAnimationParams[i] = animationParamsZeimpekiko;
            }
             
        }

        if (!useGPU) {
            
            // Real time skinning for the skeleton with hierarchy in CPU
            for (int sk=0; sk<skeletons.Length; sk++) {
                Utils.SetModelToTPose(skeletons[sk].transform);
                // skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootDance.localPosition;
                if (sk < 1) {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootDance.localPosition;
                }
                else if (sk < 3) {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootKobe.localPosition;
                }
                else {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootZeimpekiko.localPosition;
                }
            }
            // Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            // Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            allSkeletonsJointsAccumulatedRotations = Utils.AnimateMultipleSkeletonsWithHierarchy(skeletons, allAnimationParams, hierarchyBoneIdToSkeletonArrayBoneId, paths);

            // Get the animated joint positions in an array of Vector4s
            Vector3[][] animatedJointPositions = new Vector3[skeletons.Length][]; //
            // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
            for (int sk=0; sk<skeletons.Length; sk++) {
                animatedJointPositions[sk] = new Vector3[inputBones.Length];
                skeletonWithHierarchyArray = skeletons[sk].transform.GetComponentsInChildren<Transform>();

                for (int b=0; b<inputBones.Length; b++) {
                    // int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                    animatedJointPositions[sk][b] = skeletonWithHierarchyArray[hierarchyBoneIdToSkeletonArrayBoneId[b]].position;
                }
            }

            // // Vector3[] linearBlendSkinnedSkeletonMeshVerticesWithHierarchy = LinearBlendSkinningFromOffsetsWithHierarchy(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, vertexSkinningInfo);
            Vector3[][] linearBlendSkinnedSkeletonMeshesVerticesWithHierarchy = LinearBlendSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(animatedJointPositions, verticesAndJointsOffsets, vertexSkinningInfo, allSkeletonsJointsAccumulatedRotations);
            for (int sk=0; sk<skeletons.Length; sk++) {
                skeletonMeshes[sk].GetComponent<MeshFilter>().mesh.vertices = linearBlendSkinnedSkeletonMeshesVerticesWithHierarchy[sk];
            }
        }
        else {

            for (int sk=0; sk<skeletons.Length; sk++) {
                Utils.SetModelToTPose(skeletons[sk].transform);
                // skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootDance.localPosition;
                if (sk < 1) {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootDance.localPosition;
                }
                else if (sk < 3) {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootKobe.localPosition;
                }
                else {
                    skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRootZeimpekiko.localPosition;
                }
            }
            // Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            // Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);
            allSkeletonsJointsAccumulatedRotations1D = Utils.AnimateMultipleSkeletonsWithHierarchy1D(skeletons, allAnimationParams, hierarchyBoneIdToSkeletonArrayBoneId, paths);

            // Get the animated joint positions in an array of Vector4s
            Vector4[] animatedJointPositionsForShader = new Vector4[skeletons.Length * inputBones.Length]; //
            // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
            for (int sk=0; sk<skeletons.Length; sk++) {
                skeletonWithHierarchyArray = skeletons[sk].transform.GetComponentsInChildren<Transform>();

                for (int b=0; b<inputBones.Length; b++) {
                    // int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                    animatedJointPositionsForShader[b + sk * inputBones.Length] = skeletonWithHierarchyArray[hierarchyBoneIdToSkeletonArrayBoneId[b]].position;
                }
            }

            // ComputeBuffer animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));
            animatedJointPositionsBuffer.SetData(animatedJointPositionsForShader);

            // ComputeBuffer jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
            jointsAccumRotationsBuffer.SetData(allSkeletonsJointsAccumulatedRotations1D);

            LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_AnimatedJointPositions", animatedJointPositionsBuffer);
            LinearBlendSkinningMultipleCharactersComputeShader.SetBuffer(kernelId, "_JointsAccumulatedRotations", jointsAccumRotationsBuffer);
            
            LinearBlendSkinningMultipleCharactersComputeShader.Dispatch(kernelId, Mathf.CeilToInt(numberOfVertices / 1024.0f), numberOfCharacters, 1);
        }
        
    }

    public Dictionary<int, Vector3> ExtractAnimationParametersFromAnimatedModel(Transform animatedModelRoot, Dictionary<int, int> skeletonArrayBoneIdToBoneId) {

        Dictionary<int, Vector3> animationParameters = new Dictionary<int, Vector3>();

        Transform[] animatedModelJointsArray = animatedModelRoot.gameObject.GetComponentsInChildren<Transform>();

        for (int i=0; i<animatedModelJointsArray.Length; i++) {
            animationParameters[skeletonArrayBoneIdToBoneId[i]] = animatedModelJointsArray[i].localRotation.eulerAngles;
        }

        return animationParameters;
    }

    public Dictionary<int, Quaternion> ExtractAnimationParametersFromAnimatedModelInQuaternions(Transform animatedModelRoot, Dictionary<int, int> skeletonArrayBoneIdToBoneId) {

        Dictionary<int, Quaternion> animationParameters = new Dictionary<int, Quaternion>();

        Transform[] animatedModelJointsArray = animatedModelRoot.gameObject.GetComponentsInChildren<Transform>();

        for (int i=0; i<animatedModelJointsArray.Length; i++) {
            animationParameters[skeletonArrayBoneIdToBoneId[i]] = animatedModelJointsArray[i].localRotation;
        }

        return animationParameters;
    }

    public Vector3[] LinearBlendSkinningFromOffsetsWithHierarchyInWorldSpace(Vector3[] animatedJointsPositions, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo, Matrix4x4[] jointIdToAccumulatedRotations) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (jointIdToAccumulatedRotations[influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + animatedJointsPositions[influenceBoneId]);
            }

        }

        return newVerticesPositions;
    }

    public Vector3[][] LinearBlendSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(Vector3[][] animatedJointsPositions, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo, Matrix4x4[][] jointIdToAccumulatedRotations) {
        
        Vector3[][] newVerticesPositions = new Vector3[animatedJointsPositions.Length][];

        for (int sk=0; sk<animatedJointsPositions.Length; sk++) {
            newVerticesPositions[sk] = new Vector3[verticesAndJointsOffsetsInTPose.Count];
        
            // For each vertex
            for (int vid=0; vid<newVerticesPositions[sk].Length; vid++) {

                for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                    int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                    newVerticesPositions[sk][vid] += vertexSkinningInfo[vid].weights[i] * (jointIdToAccumulatedRotations[sk][influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + animatedJointsPositions[sk][influenceBoneId]);
                }
            }
        }

        return newVerticesPositions;
    }

    void OnApplicationQuit()
    {
        _verticesBuffer.Release();
        animatedJointPositionsBuffer.Release();
        verticesOffsetsAtTPoseBuffer.Release();
        influenceJointsIdsPerVertexBuffer.Release();
        skinningWeightsPerVertexBuffer.Release();
        jointsAccumRotationsBuffer.Release();
    }
}
