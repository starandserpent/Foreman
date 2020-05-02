using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GodotVector3 = Godot.Vector3;
using System.Collections.Generic;
using Threading = System.Threading.Thread;
using System;

public class Foreman {
	//This is recommend max static octree size because it takes 134 MB
	private volatile GameMesher mesher;
	private volatile Octree octree;
	private volatile int dirtID;
	private volatile int grassID;
	private int grassMeshID;
	private volatile Weltschmerz weltschmerz;
	private volatile Terra terra;
	private int maxViewDistance;
	private float fov;
	private int generationThreads;
	private List<GodotVector3> localCenters;
	private volatile bool runThread = true;
	private ConcurrentQueue<GodotVector3> centerQueue;
	private volatile List<long> chunkSpeed;
	private Threading[] threads;
	private volatile ManualResetEvent _event;
	private volatile Godot.Spatial loadMarker = null;
	private Thread GenerateThread;
	public volatile static int chunksLoaded = 0;
	public volatile static int chunksPlaced = 0;
	public volatile static int positionsScreened = 0;
	public volatile static int positionsInRange = 0;

	public volatile static int positionsNeeded = 0;

	private Godot.Transform lastTransform;

	private Stopwatch stopwatch;

	public Foreman (Weltschmerz weltschmerz, Terra terra, Registry registry, GameMesher mesher,
		int viewDistance, float fov, int generationThreads) {
		this.weltschmerz = weltschmerz;
		this.terra = terra;
		lastTransform = Godot.Transform.Identity;
		GenerateThread = new Threading (() => GenerateProcess ());
		GenerateThread.Start ();
		_event = new ManualResetEvent (true);
		this.octree = terra.GetOctree ();
		maxViewDistance = viewDistance;
		this.fov = fov;
		this.mesher = mesher;
		this.generationThreads = generationThreads;
		localCenters = new List<GodotVector3> ();
		centerQueue = new ConcurrentQueue<GodotVector3> ();
		chunkSpeed = new List<long> ();
		threads = new Threading[generationThreads];
		stopwatch = new Stopwatch ();

		for (int t = 0; t < generationThreads; t++) {
			threads[t] = new Threading (() => Process ());
			threads[t].Start ();
		}

		for (int l = -maxViewDistance; l < maxViewDistance; l += 8) {
			for (int y = -Utils.GetPosFromFOV (fov, l); y < Utils.GetPosFromFOV (fov, l); y += 8) {
				for (int x = -Utils.GetPosFromFOV (fov, l); x < Utils.GetPosFromFOV (fov, l); x += 8) {
					GodotVector3 center = new GodotVector3 (x, y, -l);
					localCenters.Add (center);
				}
			}
		}
		localCenters = localCenters.OrderBy (p => p.DistanceTo (GodotVector3.Zero)).ToList ();
	}

	private void GenerateProcess () {
		bool done = false;
		while (runThread) {
			if (loadMarker != null && !done) {
				for (int c = 0; c < localCenters.Count (); c++) {

					GodotVector3 vector3 = localCenters[c];
					GodotVector3 pos = loadMarker.ToGlobal (vector3) / 8;
					int x = (int) pos.x;
					int y = (int) pos.y;
					int z = (int) pos.z;

					if (lastTransform.Equals (loadMarker.GlobalTransform)) {
						if (x >= 0 && z >= 0 && y >= 0 && x * 8 <= octree.sizeX &&
							y * 8 <= octree.sizeY && z * 8 <= octree.sizeZ) {
							if (terra.TraverseOctree (x, y, z, 0).chunk == null) {
								centerQueue.Enqueue (pos);
							}
						}
					} else {
						centerQueue = new ConcurrentQueue<GodotVector3> ();
						lastTransform = loadMarker.GlobalTransform;
						break;
					}
				}
				done = true;
			}

			if (done && lastTransform != loadMarker.GlobalTransform) {
				centerQueue = new ConcurrentQueue<GodotVector3> ();
				lastTransform = loadMarker.GlobalTransform;
				done = false;
			}
		}
	}

	public void AddLoadMarker (Godot.Spatial loadMarker) {
		if (this.loadMarker == null) {
			this.loadMarker = loadMarker;
			this.lastTransform = loadMarker.GlobalTransform;
		}
	}

	//Initial generation

	public void Process () {
		GodotVector3 pos;
		while (runThread) {
			if (!centerQueue.IsEmpty) {
				if (centerQueue.TryDequeue (out pos)) {
					LoadArea ((int) pos.x, (int) pos.y, (int) pos.z);
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
			chunk = GenerateChunk (x << Constants.CHUNK_EXPONENT, y << Constants.CHUNK_EXPONENT,
				z << Constants.CHUNK_EXPONENT, weltschmerz);
			if (!chunk.isSurface) {
				/*
				childNode.materialID = (int) chunk.voxels[0];
				chunk.voxels = new uint[1];
				chunk.voxels[0] = (uint) childNode.materialID;
				*/
				var temp = chunk.voxels[0];
				chunk.voxels = new uint[1];
				chunk.voxels[0] = temp;
				chunk.x = (uint) x << Constants.CHUNK_EXPONENT;
				chunk.y = (uint) y << Constants.CHUNK_EXPONENT;
				chunk.z = (uint) z << Constants.CHUNK_EXPONENT;
			}
		}
		chunksPlaced++;
		terra.PlaceChunk (x, y, z, chunk);
		if (!chunk.isEmpty) {
			mesher.MeshChunk (chunk);
		}

		if (chunksPlaced >= 500) {
			stopwatch.Stop ();
			Godot.GD.Print ("500 chunks took " + stopwatch.ElapsedMilliseconds + " ms");
			chunksPlaced = 0;
			stopwatch.Restart ();
		}
	}

	public void SetMaterials (Registry registry) {
		dirtID = registry.SelectByName ("dirt").worldID;
		grassID = registry.SelectByName ("grass").worldID;
	}

	public void Stop () {
		runThread = false;
		for (int t = 0; t < generationThreads; t++) {
			threads[t].Abort ();
		}

		localCenters.Clear ();
	}

	public Chunk GenerateChunk (float posX, float posY, float posZ, Weltschmerz weltschmerz) {
		Chunk chunk = new Chunk ();

		chunk.x = (uint) posX;
		chunk.y = (uint) posY;
		chunk.z = (uint) posZ;

		chunk.materials = 1;

		uint[] voxels = ArrayPool<uint>.Shared.Rent (Constants.CHUNK_SIZE3D);

		chunk.isEmpty = true;

		int posx = (int) (posX * 4);
		int posz = (int) (posZ * 4);
		int posy = (int) (posY * 4);

		int lastPosition = 0;

		chunk.isSurface = false;
		for (int z = 0; z < Constants.CHUNK_SIZE1D; z++) {
			for (int x = 0; x < Constants.CHUNK_SIZE1D; x++) {
				int elevation = (int) weltschmerz.GetElevation (x + posx, z + posz);

				if (elevation / Constants.CHUNK_SIZE1D == posy / Constants.CHUNK_SIZE1D) {
					int elev = elevation % Constants.CHUNK_SIZE1D;
					uint bitPos;
					uint bitValue;
					bitPos = (uint) elev << 8;
					bitValue = (uint) dirtID;

					voxels[lastPosition] = (bitPos | bitValue);

					lastPosition++;

					bitPos = (uint) 1 << 8;
					bitValue = (uint) grassID;

					voxels[lastPosition] = (bitPos | bitValue);

					lastPosition++;
					bitPos = (uint) (Constants.CHUNK_SIZE1D - elev - 1) << 8;
					bitValue = (uint) 0;

					voxels[lastPosition] = (bitPos | bitValue);

					lastPosition++;

					chunk.isSurface = true;
					chunk.isEmpty = false;
				} else if (elevation / Constants.CHUNK_SIZE1D > posy / Constants.CHUNK_SIZE1D) {
					uint bitPos = (uint) (Constants.CHUNK_SIZE1D) << 8;
					uint bitValue = (uint) dirtID;
					chunk.isEmpty = false;

					voxels[lastPosition] = (bitPos | bitValue);

					lastPosition++;
				} else if (elevation / Constants.CHUNK_SIZE1D < posy / Constants.CHUNK_SIZE1D) {
					uint bitPos = (uint) (Constants.CHUNK_SIZE1D) << 8;
					uint bitValue = (uint) 0;

					voxels[lastPosition] = (bitPos | bitValue);

					lastPosition++;
				}
			}
		}

		if (chunk.isSurface) {
			chunk.materials = 3;
			chunk.voxels = new uint[lastPosition];
			Array.ConstrainedCopy (voxels, 0, chunk.voxels, 0, lastPosition);
			ArrayPool<uint>.Shared.Return (voxels);
		} else {
			if (chunk.isEmpty) {
				chunk.voxels = new uint[1] { 0 };
			} else {
				chunk.voxels = new uint[1] {
					(uint) dirtID
				};
			}
		}
		return chunk;
	}

	public List<long> GetMeasures () {
		return chunkSpeed;
	}
}