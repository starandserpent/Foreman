using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
public class Foreman {
	//This is recommend max static octree size because it takes 134 MB
	private volatile GodotMesher mesher;
	private volatile Octree octree;
	//private int grassMeshID;
	private volatile Weltschmerz weltschmerz;
	private volatile Terra terra;
	private int maxViewDistance;
	private float fov;
	private ChunkFiller chunkFiller;
	private ConcurrentQueue<Tuple<int, int, int>> queue;
	private Position[] localCenters;
	private volatile bool runThread = true;
	private volatile List<long> chunkSpeed;
	private volatile Godot.Spatial loadMarker = null;
	public volatile static int chunksLoaded = 0;
	public volatile static int chunksPlaced = 0;
	public volatile static int positionsScreened = 0;
	public volatile static int positionsInRange = 0;

	public volatile static int positionsNeeded = 0;

	private ConcurrentDictionary<Tuple<int, int, int>, OctreeNode> leafOctants;

	private Stopwatch stopwatch;
	private Semaphore preparation;
	private Semaphore generation;

	private int Length = 0;

	private int maxSize;

	private TerraVector3[] basis;
	private TerraVector3 origin;

	private int generationThreads;

	public Foreman (Weltschmerz weltschmerz, Terra terra, Registry registry, GodotMesher mesher,
		int viewDistance, float fov, int generationThreads) {
		this.generationThreads = generationThreads;
		this.weltschmerz = weltschmerz;
		this.terra = terra;
		this.octree = terra.GetOctree ();
		queue = new ConcurrentQueue<Tuple<int, int, int>> ();
		maxViewDistance = viewDistance;
		this.fov = fov;
		this.mesher = mesher;
		chunkSpeed = new List<long> ();
		stopwatch = new Stopwatch ();
		this.leafOctants = new ConcurrentDictionary<Tuple<int, int, int>, OctreeNode> ();

		List<Position> localCenters = new List<Position> ();
		for (int l = -maxViewDistance; l < maxViewDistance; l += 8) {
			for (int y = -Utils.GetPosFromFOV (fov, l); y < Utils.GetPosFromFOV (fov, l); y += 8) {
				for (int x = -Utils.GetPosFromFOV (fov, l); x < Utils.GetPosFromFOV (fov, l); x += 8) {
					localCenters.Add (new Position (x, y, -l));
				}
			}
		}

		this.localCenters = localCenters.ToArray ();
		localCenters.Clear ();

		maxSize = this.localCenters.Length;

		this.preparation = new Semaphore ();
		this.generation = new Semaphore ();
	}

	public void Release () {
		basis = new TerraVector3[3];

		queue = new ConcurrentQueue<Tuple<int, int, int>> ();
		Length = 0;

		for (int i = 0; i < generationThreads; i++) {
			preparation.Post ();
		}

		origin = new TerraVector3 (loadMarker.Transform.origin.x, loadMarker.Transform.origin.y, loadMarker.Transform.origin.z);

		basis[0] = new TerraVector3 (loadMarker.Transform.basis[0].x, loadMarker.Transform.basis[0].y, loadMarker.Transform.basis[0].z);
		basis[1] = new TerraVector3 (loadMarker.Transform.basis[1].x, loadMarker.Transform.basis[1].y, loadMarker.Transform.basis[1].z);
		basis[2] = new TerraVector3 (loadMarker.Transform.basis[2].x, loadMarker.Transform.basis[2].y, loadMarker.Transform.basis[2].z);
	}

	public void AddLoadMarker (Spatial loadMarker) {
		if (this.loadMarker == null) {
			this.loadMarker = loadMarker;
			Release ();
		}
	}

	//Initial generation

	public void Process () {
		while (runThread) {
			preparation.Wait ();
			if (Length < maxSize) {

				Position pos = localCenters[Length];

				Godot.Vector3 lol = new Godot.Vector3 (pos.x, pos.y, pos.z);

				/*	Godot.Vector3 position = new Godot.Vector3 (
						(basis[0].x * lol.x + basis[0].y * lol.y + basis[0].z * lol.z) + origin.x,
						(basis[1].x * lol.x + basis[1].y * lol.y + basis[1].z * lol.z) + origin.y,
						(basis[2].x * lol.x + basis[2].y * lol.y + basis[2].z * lol.z) + origin.z) / 8;
					//origin = ToGlobal (origin, basis, pos) / 8;*/
				Godot.Vector3 position = new Godot.Vector3 ();

				lock (this) {
					position = loadMarker.ToGlobal (lol) / 8;
				}

				pos.x = (int) position.x;
				pos.y = (int) position.y;
				pos.z = (int) position.z;

				Tuple<int, int, int> key = new Tuple<int, int, int> (pos.x, pos.y, pos.z);
				OctreeNode node = null;
				if (!leafOctants.ContainsKey (key) || leafOctants.TryGetValue (key, out node)) {
					if (node == null) {
						node = terra.TraverseOctree (pos.x, pos.y, pos.z, 0);
					}

					if (node != null && node.chunk == null) {
						leafOctants.TryAdd (key, node);
						queue.Enqueue (key);
						generation.Post ();
					}
				}

				preparation.Post ();

				Length++;
			}
		}
	}

	public void Generate () {
		ArrayPool<Position> pool = ArrayPool<Position>.Create (Constants.CHUNK_SIZE3D * 4 * 6, 1);
		while (runThread) {
			generation.Wait ();
			Tuple<int, int, int> pos;
			OctreeNode node;
			if (queue.TryDequeue (out pos)) {
				if (terra.CheckBoundries (pos.Item1, pos.Item2, pos.Item3) && leafOctants.TryGetValue (pos, out node)) {
					LoadArea (pos, node, pool);
				}
			}
		}
	}

	//Loads chunks
	private void LoadArea (Tuple<int, int, int> pos, OctreeNode node, ArrayPool<Position> pool) {

		Chunk chunk;
		if (pos.Item2 << Constants.CHUNK_EXPONENT > weltschmerz.GetConfig ().elevation.max_elevation) {
			chunk = new Chunk ();
			chunk.IsEmpty = true;
			chunk.x = (uint) pos.Item1 << Constants.CHUNK_EXPONENT;
			chunk.y = (uint) pos.Item2 << Constants.CHUNK_EXPONENT;
			chunk.z = (uint) pos.Item3 << Constants.CHUNK_EXPONENT;
		} else {
			chunk = chunkFiller.GenerateChunk (pos.Item1 << Constants.CHUNK_EXPONENT, pos.Item2 << Constants.CHUNK_EXPONENT,
				pos.Item3 << Constants.CHUNK_EXPONENT, weltschmerz);
			if (!chunk.IsSurface) {
				var temp = chunk.Voxels[0];
				chunk.Voxels = new Run[1];
				chunk.Voxels[0] = temp;
				chunk.x = (uint) pos.Item1 << Constants.CHUNK_EXPONENT;
				chunk.y = (uint) pos.Item2 << Constants.CHUNK_EXPONENT;
				chunk.z = (uint) pos.Item3 << Constants.CHUNK_EXPONENT;
			}
		}

		if (!chunk.IsEmpty) {
			mesher.MeshChunk (chunk, pool);
			chunksPlaced++;
		}

		node.chunk = chunk;
	}

	public void SetMaterials (Registry registry) {
		chunkFiller = new ChunkFiller (registry.SelectByName ("dirt").worldID, registry.SelectByName ("grass").worldID);
	}

	public void Stop () {
		runThread = false;
	}

	private TerraVector3 ToGlobal (TerraVector3 origin, TerraVector3[] basis, Position coords) {
		TerraVector3 pos = new TerraVector3 ();
		pos.x = (int) (basis[0].Dot (coords) + origin.x);
		pos.y = (int) (basis[1].Dot (coords) + origin.y);
		pos.z = (int) (basis[2].Dot (coords) + origin.z);
		return pos;
	}

	public List<long> GetMeasures () {
		return chunkSpeed;
	}
}