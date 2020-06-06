using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
public class Foreman {
	//This is recommend max static octree size because it takes 134 MB
	private volatile ITerraMesher mesher;
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
	private volatile LoadMarker loadMarker = null;
	public volatile static int chunksLoaded = 0;
	public volatile static int chunksPlaced = 0;
	public volatile static int positionsScreened = 0;
	public volatile static int positionsInRange = 0;

	public volatile static int positionsNeeded = 0;

	private ConcurrentDictionary<Tuple<int, int, int>, OctreeNode> leafOctants;

	private Stopwatch stopwatch;
	private ITerraSemaphore preparation;
	private ITerraSemaphore generation;

	private int Length = 0;

	private int maxSize;

	private int generationThreads;

	public Foreman (Weltschmerz weltschmerz, Terra terra, Registry registry, ITerraMesher mesher,
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
	}

	public void Release () {
		queue = new ConcurrentQueue<Tuple<int, int, int>> ();
		Length = 0;

		for (int i = 0; i < generationThreads; i++) {
			preparation.Post ();
		}
	}

	public void AddLoadMarker (LoadMarker loadMarker, ITerraSemaphore preparation, ITerraSemaphore generation) {
	
		this.preparation = preparation;
		this.generation = generation;
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
				pos =  loadMarker.ToGlobal(pos) / 8;

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

	public List<long> GetMeasures () {
		return chunkSpeed;
	}
}