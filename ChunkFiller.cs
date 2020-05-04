using System;
using System.Buffers;
using System.Collections.Generic;
public class ChunkFiller {
    private volatile int dirtID;
    private volatile int grassID;
    public ChunkFiller (int dirtID, int grassID) {
        this.dirtID = dirtID;
        this.grassID = grassID;
    }
    public Chunk GenerateChunk (float posX, float posY, float posZ, Weltschmerz weltschmerz) {
        Chunk chunk = new Chunk ();

        chunk.x = (uint) posX;
        chunk.y = (uint) posY;
        chunk.z = (uint) posZ;

        chunk.materials = 1;

        Stack<Run> voxels = new Stack<Run> (Constants.CHUNK_SIZE3D);

        chunk.isEmpty = true;

        int posx = (int) (posX * 4);
        int posz = (int) (posZ * 4);
        int posy = (int) (posY * 4);

        Run run = new Run ();

        chunk.isSurface = false;
        for (int z = 0; z < Constants.CHUNK_SIZE1D; z++) {
            for (int x = 0; x < Constants.CHUNK_SIZE1D; x++) {
                int elevation = (int) weltschmerz.GetElevation (x + posx, z + posz);

                if (elevation / Constants.CHUNK_SIZE1D == posy / Constants.CHUNK_SIZE1D) {

                    if (elevation > 0) {
                        int position = elevation % Constants.CHUNK_SIZE1D;

                        if (voxels.Count > 0) {
                            run = voxels.Pop ();
                            if (run.value == dirtID) {
                                run.lenght = position + run.lenght;
                                voxels.Push (run);
                            } else {
                                voxels.Push (run);
                                run = new Run ();
                                run.lenght = position;
                                run.value = dirtID;
                                voxels.Push (run);
                            }
                        } else {
                            run = new Run ();
                            run.lenght = position;
                            run.value = dirtID;
                            voxels.Push (run);
                        }

                        run = new Run ();
                        run.lenght = 1;
                        run.value = grassID;

                        voxels.Push (run);

                        run = new Run ();
                        run.lenght = Constants.CHUNK_SIZE1D - (elevation % Constants.CHUNK_SIZE1D) - 1;
                        run.value = 0;

                        voxels.Push (run);

                        chunk.isSurface = true;
                        chunk.isEmpty = false;
                    } else {
                        int position = Constants.CHUNK_SIZE1D;
                        if (voxels.Count > 0) {
                            run = voxels.Pop ();
                            if (run.value == 0) {
                                run.lenght = position + run.lenght;
                                voxels.Push (run);
                                continue;
                            }

                            voxels.Push (run);
                        }

                        run = new Run ();
                        run.lenght = position;
                        run.value = 0;

                        voxels.Push (run);
                    }
                } else if (elevation / Constants.CHUNK_SIZE1D > posy / Constants.CHUNK_SIZE1D) {

                    int position = Constants.CHUNK_SIZE1D;

                    if (voxels.Count > 0) {
                        run = voxels.Pop ();
                        if (run.value == dirtID) {
                            run.lenght = position + run.lenght;
                            voxels.Push (run);
                            continue;
                        }

                        voxels.Push (run);
                    }

                    run = new Run ();
                    run.lenght = position;
                    run.value = dirtID;

                    voxels.Push (run);

                    chunk.isEmpty = false;

                } else if (elevation / Constants.CHUNK_SIZE1D < posy / Constants.CHUNK_SIZE1D) {

                    int position = Constants.CHUNK_SIZE1D;

                    if (voxels.Count > 0) {

                        run = voxels.Pop ();
                        if (run.value == 0) {
                            run.lenght = position + run.lenght;
                            voxels.Push (run);
                            continue;
                        }

                        voxels.Push (run);
                    }

                    run = new Run ();
                    run.lenght = position;
                    run.value = 0;

                    voxels.Push (run);
                }
            }
        }

        if (chunk.isSurface) {
            chunk.materials = 3;
            chunk.voxels = voxels.ToArray ();
        } else {
            run = new Run ();
            if (chunk.isEmpty) {
                run.value = 0;
                chunk.voxels = new Run[1] { run };
            } else {
                run.value = dirtID;
                chunk.voxels = new Run[1] { run };
            }
        }

        return chunk;
    }

}