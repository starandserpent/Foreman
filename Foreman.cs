using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using GodotVector3 = Godot.Vector3;
using System.Collections.Generic;
using Threading = System.Threading.Thread;

public class Foreman {
	//This is recommend max static octree size because it takes 134 MB
	private volatile GodotMesher mesher;
	private volatile Octree octree;
	private int grassMeshID;
	private volatile Weltschmerz weltschmerz;
	private volatile Terra terra;
	private int maxViewDistance;
	private float fov;
	private int generationThreads;
	private ChunkFiller chunkFiller;
	private ConcurrentQueue<Position> queue;
	private Position[] localCenters;
	private volatile bool runThread = true;
	private volatile List<long> chunkSpeed;
	private Threading[] threads;
	private volatile Godot.Spatial loadMarker = null;
	public volatile static int chunksLoaded = 0;
	public volatile static int chunksPlaced = 0;
	public volatile static int positionsScreened = 0;
	public volatile static int positionsInRange = 0;

	public volatile static int positionsNeeded = 0;

	private Godot.Transform lastTransform;

	private Stopwatch stopwatch;
	private SemaphoreSlim preparation;
	private SemaphoreSlim generation;

	private int Length = 0;

	private int maxSize;

	private Threading processThread;

	public Foreman (Weltschmerz weltschmerz, Terra terra, Registry registry, GodotMesher mesher,
		int viewDistance, float fov, int generationThreads) {
		this.weltschmerz = weltschmerz;
		this.terra = terra;
		lastTransform = Godot.Transform.Identity;
		this.octree = terra.GetOctree ();
		queue = new ConcurrentQueue<Position> ();
		maxViewDistance = viewDistance;
		this.fov = fov;
		this.mesher = mesher;
		this.generationThreads = generationThreads;
		chunkSpeed = new List<long> ();
		threads = new Threading[generationThreads];
		stopwatch = new Stopwatch ();

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

		this.preparation = new SemaphoreSlim (0, 3);
		this.generation = new SemaphoreSlim (0, generationThreads);

		for (int t = 0; t < generationThreads; t++) {
			threads[t] = new Threading (() => Generate ());
			threads[t].Start ();
		}

		processThread = new Threading (() => Process ());
		processThread.Start ();
	}

	public void Release () {
		queue = new ConcurrentQueue<Position> ();
		Length = 0;
		if (preparation.CurrentCount < 1) {
			preparation.Release (1);
		}
	}

	public void AddLoadMarker (Godot.Spatial loadMarker) {
		if (this.loadMarker == null) {
			this.loadMarker = loadMarker;
			this.lastTransform = loadMarker.GlobalTransform;
			Release ();
		}
	}

	//Initial generation

	private void Process () {
		while (runThread) {
			preparation.Wait ();
			if (Length < maxSize) {
				Position pos = localCenters[Length];
				GodotVector3 position = new GodotVector3 (pos.x, pos.y, pos.z);
				position = loadMarker.ToGlobal (position) / 8;
				pos.x = (int) position.x;
				pos.y = (int) position.y;
				pos.z = (int) position.z;
				OctreeNode node;
				node = terra.TraverseOctree (pos.x, pos.y, pos.z, 0);
				if (node != null && node.chunk == null) {
					queue.Enqueue (pos);
					if (generation.CurrentCount < generationThreads) {
						generation.Release (1);
					}
				}

				if (preparation.CurrentCount < 1) {
					preparation.Release (1);
				}

				Length++;
			}
		}
	}

	public void Generate () {
		while (runThread) {
			generation.Wait ();
			Position pos;
			if (queue.TryDequeue (out pos)) {
				if (terra.CheckBoundries (pos.x, pos.y, pos.z)) {
					LoadArea (pos.x, pos.y, pos.z);
				}
			}
		}
	}

	//Loads chunks
	private void LoadArea (int x, int y, int z) {
		//OctreeNode childNode = new OctreeNode();
		if (chunksPlaced < 1) {
			stopwatch.Start ();
		}

		Chunk chunk;
		if (y << Constants.CHUNK_EXPONENT > weltschmerz.GetConfig ().elevation.max_elevation) {
			chunk = new Chunk ();
			chunk.isEmpty = true;
			chunk.x = (uint) x << Constants.CHUNK_EXPONENT;
			chunk.y = (uint) y << Constants.CHUNK_EXPONENT;
			chunk.z = (uint) z << Constants.CHUNK_EXPONENT;
		} else {
			chunk = chunkFiller.GenerateChunk (x << Constants.CHUNK_EXPONENT, y << Constants.CHUNK_EXPONENT,
				z << Constants.CHUNK_EXPONENT, weltschmerz);
			if (!chunk.isSurface) {
				var temp = chunk.voxels[0];
				chunk.voxels = new Run[1];
				chunk.voxels[0] = temp;
				chunk.x = (uint) x << Constants.CHUNK_EXPONENT;
				chunk.y = (uint) y << Constants.CHUNK_EXPONENT;
				chunk.z = (uint) z << Constants.CHUNK_EXPONENT;
			}
		}
		terra.PlaceChunk (x, y, z, chunk);
		if (!chunk.isEmpty) {
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			mesher.MeshChunk (chunk);
			stopwatch.Stop();
			Godot.GD.Print("chunk meshed in " + stopwatch.ElapsedMilliseconds);
			chunksPlaced++;
		}
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