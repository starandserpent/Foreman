using System;
public class LoadMarker : Marker {
    public Foreman foreman;

    public LoadMarker () {
        this.origin = new TerraVector3 ();
        this.basis = TerraBasis.InitEmpty();
    }

    public LoadMarker (TerraVector3 origin, TerraBasis basis) {
        this.origin = origin;
        this.basis = basis;
    }

    public LoadMarker (TerraVector3 origin) {
        this.origin = origin;
        this.basis = TerraBasis.InitEmpty();
    }

    public LoadMarker (int x, int y, int z) {
        this.origin = new TerraVector3 (x, y, z);
        this.basis = TerraBasis.InitEmpty();
    }

    public void Attach (Foreman foreman) {
        this.foreman = foreman;
    }

    public override void ChangePosition (TerraVector3 origin, TerraBasis basis) {
        this.origin = origin;
        this.basis = basis;

        foreman.Release ();
    }

    public override void Move (TerraVector3 origin) {
        this.origin = this.origin + origin;
        foreman.Release ();
    }

    public void MoveTo (TerraVector3 origin) {
        this.origin = origin;
        foreman.Release ();
    }

    public override void Rotate (TerraBasis basis) {
        this.basis = this.basis * basis;
        foreman.Release ();
    }
}