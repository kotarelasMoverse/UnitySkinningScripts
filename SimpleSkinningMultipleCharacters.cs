using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UIElements;
using Unity.VisualScripting;
using System.Linq;


public class SimpleSkinningMultipleCharacters : MonoBehaviour
{
    public int numberOfCharacters=1;
    public bool useGPU = false;
    Transform animatedModelRoot;
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
    Dictionary<int, Quaternion> animationParams;
    Matrix4x4[] jointsAccumulatedRotations;
    Matrix4x4[][] allSkeletonsJointsAccumulatedRotations;
    Matrix4x4[] allSkeletonsJointsAccumulatedRotations1D;
    Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsets;

    public ComputeShader SimpleSkinningMultipleCharactersComputeShader;
    private ComputeBuffer _verticesBuffer;
    ComputeBuffer animatedJointPositionsBuffer;
    // ComputeBuffer verticesInfoBuffer;
    ComputeBuffer verticesOffsetsAtTPoseBuffer;
    ComputeBuffer influenceJointsIdsPerVertexBuffer;
    ComputeBuffer jointsAccumRotationsBuffer;

    List<int[]> paths = new List<int[]>
        {
            new int[] { 0, 1, 4, 7, 10 },
            new int[] { 0, 2, 5, 8, 11 },
            new int[] { 0, 3, 6, 9, 12, 15 },
            new int[] { 0, 3, 6, 9, 13, 16, 18, 20, 22 },
            new int[] { 0, 3, 6, 9, 14, 17, 19, 21, 23 }
        };

    // Start is called before the first frame update
    void Start()
    {
        Transform root = GameObject.Find("Clone").transform.GetChild(0).GetChild(0); // The transform of the root joint (pelvis) game object. From this we will create the rest of the characters.
        SkinnedMeshRenderer rend = root.GetComponentInParent<SkinnedMeshRenderer>(); // The skinned mesh renderer of the "Clone character"
        GameObject.Find("Clone").SetActive(false);

        animatedModelRoot = GameObject.Find("Free-Dance-Movements").transform.GetChild(0).GetChild(0);

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
        skeletonWithHierarchyArray = skeletons[0].transform.GetComponentsInChildren<Transform>();
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
            skeletonMeshes[i] = Utils.CreateNewMeshAtPosition(skeletons[i].transform, inputVerticesRootCoord);
            skeletonMeshes[i].name = "SimplekinningMeshCharacter" + $"{i}";
        }

        for (int i=0; i<numberOfCharacters; i++) {
            skeletons[i].transform.position += new Vector3(i*2, 0, 0);
            initialSkeletonsPositions[i] = skeletons[i].transform.position;
        }

        // Extract the animation parameters from the character model that is moved by the animator
        animationParams = ExtractAnimationParametersFromAnimatedModelInQuaternions(animatedModelRoot, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Quaternion>[] allAnimationParams = new Dictionary<int, Quaternion>[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) { allAnimationParams[i] = animationParams; }

        // Apply the animation parameters to the skeleton with hierarchy
        // Utils.AnimateSkeletonWithHierarchy(skeletons[0].transform, animationParams, inputBones);

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        // jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

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

        Vector3[][] simpleSkinnedSkeletonMeshesVerticesWithHierarchy = SimpleSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(animatedJointPositions, verticesAndJointsOffsets, allSkeletonsJointsAccumulatedRotations);
        for (int sk=0; sk<skeletons.Length; sk++) {
            skeletonMeshes[sk].GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshesVerticesWithHierarchy[sk];
        }
        

        #region GPU initializations
        // Prepare data and compute buffers with static data
        
        Vector4[] verticesOffsetsAtTPoseForShader = new Vector4[numberOfVertices * numberOfCharacters];
        int[] influenceJointsIdsPerVertexForShader = new int[numberOfVertices * numberOfCharacters];        

        for (int sk=0; sk<numberOfCharacters; sk++) {
            for (int vid=0; vid<numberOfVertices; vid++) {
                int influenceBoneId = verticesAndJointsOffsets[vid].Keys.ToList()[0];
                verticesOffsetsAtTPoseForShader[vid + sk * numberOfVertices] = verticesAndJointsOffsets[vid][influenceBoneId];
                influenceJointsIdsPerVertexForShader[vid + sk * numberOfVertices] = influenceBoneId;
            }
        }
        // Initialize the vertices buffer
        _verticesBuffer = new ComputeBuffer(numberOfVertices * 3 * numberOfCharacters, sizeof(float));

        animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length * 4 * numberOfCharacters, sizeof(float));

        verticesOffsetsAtTPoseBuffer = new ComputeBuffer(numberOfVertices * 4 * numberOfCharacters, sizeof(float));
        verticesOffsetsAtTPoseBuffer.SetData(verticesOffsetsAtTPoseForShader);

        influenceJointsIdsPerVertexBuffer = new ComputeBuffer(numberOfVertices * numberOfCharacters, sizeof(int));
        influenceJointsIdsPerVertexBuffer.SetData(influenceJointsIdsPerVertexForShader);

        jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length * 16 * numberOfCharacters, sizeof(float));
        #endregion
    }

    // Update is called once per frame
    void Update()
    {
        // Extract the animation parameters from the character model that is moved by the animator
        Dictionary<int, Quaternion> animationParams = ExtractAnimationParametersFromAnimatedModelInQuaternions(animatedModelRoot, skeletonArrayBoneIdToHierarchyBoneId);
        Dictionary<int, Quaternion>[] allAnimationParams = new Dictionary<int, Quaternion>[numberOfCharacters];
        for (int i=0; i<numberOfCharacters; i++) { allAnimationParams[i] = animationParams; }

        if (!useGPU) {
            
            // Real time skinning for the skeleton with hierarchy in CPU
            for (int sk=0; sk<skeletons.Length; sk++) {
                Utils.SetModelToTPose(skeletons[sk].transform);
                skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRoot.localPosition;
            }
            // Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            // Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            allSkeletonsJointsAccumulatedRotations = Utils.AnimateMultipleSkeletonsWithHierarchy(skeletons, allAnimationParams, hierarchyBoneIdToSkeletonArrayBoneId, paths);
            // Utils.AnimateSkeletonWithHierarchy(skeletons[0].transform, animationParams, inputBones);

            // // Get the accumulated rotations that each joint will impose to the vertices that it influences
            // jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

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
            Vector3[][] simpleSkinnedSkeletonMeshesVerticesWithHierarchy = SimpleSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(animatedJointPositions, verticesAndJointsOffsets, allSkeletonsJointsAccumulatedRotations);
            for (int sk=0; sk<skeletons.Length; sk++) {
                skeletonMeshes[sk].GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshesVerticesWithHierarchy[sk];
            }
        }
        else {

            for (int sk=0; sk<skeletons.Length; sk++) {
                Utils.SetModelToTPose(skeletons[sk].transform);
                skeletons[sk].transform.position = initialSkeletonsPositions[sk] + animatedModelRoot.localPosition;
            }
            // // Get the accumulated rotations that each joint will impose to the vertices that it influences
            allSkeletonsJointsAccumulatedRotations1D = Utils.AnimateMultipleSkeletonsWithHierarchy1D(skeletons, allAnimationParams, hierarchyBoneIdToSkeletonArrayBoneId, paths);
            
            // Get the animated joint positions in an array of Vector3s
            Vector4[] animatedJointPositionsForShader = new Vector4[skeletons.Length * inputBones.Length]; //
            // skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
            for (int sk=0; sk<skeletons.Length; sk++) {
                skeletonWithHierarchyArray = skeletons[sk].transform.GetComponentsInChildren<Transform>();

                for (int b=0; b<inputBones.Length; b++) {
                    // int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                    animatedJointPositionsForShader[b + sk * inputBones.Length] = skeletonWithHierarchyArray[hierarchyBoneIdToSkeletonArrayBoneId[b]].position;
                }
            }

            // // ComputeBuffer animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));
            animatedJointPositionsBuffer.SetData(animatedJointPositionsForShader);

            // // ComputeBuffer jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
            jointsAccumRotationsBuffer.SetData(allSkeletonsJointsAccumulatedRotations1D);

            float[] flattenedVertices = new float[numberOfVertices * 3 * numberOfCharacters];


            int kernel = SimpleSkinningMultipleCharactersComputeShader.FindKernel("SimpleSkinningMultipleCharacters");
            SimpleSkinningMultipleCharactersComputeShader.SetBuffer(kernel, "_VertexBuffer", _verticesBuffer);
            SimpleSkinningMultipleCharactersComputeShader.SetInt("_NumVertices", numberOfVertices);
            SimpleSkinningMultipleCharactersComputeShader.SetInt("_NumCharacters", numberOfCharacters);
            SimpleSkinningMultipleCharactersComputeShader.SetInt("_NumJoints", inputBones.Length);
            SimpleSkinningMultipleCharactersComputeShader.SetBuffer(kernel, "_AnimatedJointPositions", animatedJointPositionsBuffer);
            SimpleSkinningMultipleCharactersComputeShader.SetBuffer(kernel, "_VerticesOffsetsWithJoints", verticesOffsetsAtTPoseBuffer);
            SimpleSkinningMultipleCharactersComputeShader.SetBuffer(kernel, "_IdsOfInfluenceJointsPerVertex", influenceJointsIdsPerVertexBuffer);
            SimpleSkinningMultipleCharactersComputeShader.SetBuffer(kernel, "_JointsAccumulatedRotations", jointsAccumRotationsBuffer);


            SimpleSkinningMultipleCharactersComputeShader.Dispatch(kernel, Mathf.CeilToInt(numberOfVertices/1024.0f), numberOfCharacters, 1);
            _verticesBuffer.GetData(flattenedVertices);

            // From flattened to vector3 array
            Vector3[] newVertices;
            for (int sk=0; sk<numberOfCharacters; sk++) {
                newVertices = new Vector3[numberOfVertices];
                for (int vid=0; vid<numberOfVertices; vid++) {
                    newVertices[vid] = new Vector3(flattenedVertices[vid*3 + sk * numberOfVertices * 3], flattenedVertices[vid*3 + 1 + sk * numberOfVertices * 3], flattenedVertices[vid*3 + 2 + sk * numberOfVertices * 3]);
                }
                skeletonMeshes[sk].GetComponent<MeshFilter>().mesh.vertices = newVertices;
            }
        }
        
    }

    public Dictionary<int, Vector3> ExtractAnimationParametersFromAnimatedModel(Transform animatedModelRoot, Dictionary<int, int> skeletonArrayBoneIdToBoneId) {

        Dictionary<int, Vector3> animationParameters = new Dictionary<int, Vector3>();

        Transform[] animatedModelJointsArray = animatedModelRoot.GetComponentsInChildren<Transform>();

        for (int i=0; i<animatedModelJointsArray.Length; i++) {
            animationParameters[skeletonArrayBoneIdToBoneId[i]] = animatedModelJointsArray[i].localRotation.eulerAngles;
        }

        return animationParameters;
    }

    public Dictionary<int, Quaternion> ExtractAnimationParametersFromAnimatedModelInQuaternions(Transform animatedModelRoot, Dictionary<int, int> skeletonArrayBoneIdToBoneId) {

        Dictionary<int, Quaternion> animationParameters = new Dictionary<int, Quaternion>();

        Transform[] animatedModelJointsArray = animatedModelRoot.GetComponentsInChildren<Transform>();

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

    public Vector3[][] SimpleSkinningMultipleSkeletonsFromOffsetsWithHierarchyInWorldSpace(Vector3[][] animatedJointsPositions, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Matrix4x4[][] jointIdToAccumulatedRotations) {
        
        Vector3[][] newVerticesPositions = new Vector3[animatedJointsPositions.Length][];

        for (int sk=0; sk<animatedJointsPositions.Length; sk++) {
            newVerticesPositions[sk] = new Vector3[verticesAndJointsOffsetsInTPose.Count];

            // For each vertex
            for (int vid=0; vid<newVerticesPositions[sk].Length; vid++) {

                // Get the id of the bone that influences this vertex, with the largest weight (first bone)
                int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];

                // Calculate the new position of the vertex by adding the offset to the position of the bone that it is infuenced by
                // The offsets are calculated wrt the root joint, so the joint position should also be converted to coordinates wrt the root joint.
                newVerticesPositions[sk][vid] = jointIdToAccumulatedRotations[sk][influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + animatedJointsPositions[sk][influenceBoneId];
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
    //     jointsAccumRotationsBuffer.Release();
    // }
}
