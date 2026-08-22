namespace FiveStack.Entities.Practice;

// One smoke's measured density grid, in the same EventSmokeVolume shape the
// demo playback blob carries.
//
// den is base64, two cells per byte with the low nibble first, over dx*dy*dz
// cells, x-major then y then z: cell (i,j,k) is at index (k*dy + j)*dx + i and
// has its minimum corner at (ox,oy,oz) + (i,j,k)*vs, in source units. 0 is
// clear, 15 is fully dense.
public class SmokeVolume
{
    public float ox { get; set; }
    public float oy { get; set; }
    public float oz { get; set; }

    public float vs { get; set; }

    public int dx { get; set; }
    public int dy { get; set; }
    public int dz { get; set; }

    public string? den { get; set; }
}
